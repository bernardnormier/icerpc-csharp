// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::*;
use slice::grammar::*;

use crate::code_block::CodeBlock;
use crate::cs_util::*;
use crate::slicec_ext::*;

pub fn encode_data_members(
    members: &[&DataMember],
    namespace: &str,
    field_type: FieldType,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let (required_members, tagged_members) = get_sorted_members(members);

    let mut bit_sequence_index = -1;
    // Tagged members are encoded in a dictionary and don't count towards the optional bit sequence
    // size.
    let bit_sequence_size = get_bit_sequence_size(&required_members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequence = encoder.EncodeBitSequence({});",
            bit_sequence_size
        );
        bit_sequence_index = 0;
    }

    for member in required_members {
        let param = format!("this.{}", member.field_name(field_type));

        let encode_member = encode_type(
            member.data_type(),
            &mut bit_sequence_index,
            true,
            namespace,
            &param,
        );
        code.writeln(&encode_member);
    }

    // Encode tagged
    for member in tagged_members {
        let param = format!("this.{}", member.field_name(field_type));
        code.writeln(&encode_tagged_type(member, namespace, &param, true));
    }

    code
}

pub fn encode_type(
    type_ref: &TypeRef,
    bit_sequence_index: &mut i64,
    for_nested_type: bool,
    namespace: &str,
    param: &str,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let value = if type_ref.is_optional && type_ref.is_value_type() {
        format!("{}.Value", param)
    } else {
        param.to_owned()
    };

    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            writeln!(code, "encoder.EncodeProxy({}.Proxy);", value)
        }
        TypeRefs::Class(_) => {
            writeln!(code, "encoder.EncodeClass({});", value)
        }
        TypeRefs::Primitive(primitive_ref) => {
            writeln!(
                code,
                "encoder.Encode{}({});",
                primitive_ref.type_suffix(),
                value
            )
        }
        TypeRefs::Struct(_) => {
            writeln!(code, "{}.Encode(encoder);", value)
        }
        TypeRefs::Sequence(sequence_ref) => writeln!(
            code,
            "{};",
            encode_sequence(
                sequence_ref,
                namespace,
                param,
                !for_nested_type,
                !for_nested_type,
            ),
        ),
        TypeRefs::Dictionary(dictionary_ref) => {
            writeln!(
                code,
                "{};",
                encode_dictionary(dictionary_ref, namespace, param)
            )
        }
        TypeRefs::Enum(enum_ref) => {
            writeln!(
                code,
                "{helper}.Encode{name}(encoder, {param});",
                helper = enum_ref.helper_name(namespace),
                name = enum_ref.identifier(),
                param = value,
            );
        }
    }

    if type_ref.is_optional {
        code = encode_as_optional(type_ref, bit_sequence_index, for_nested_type, param, &code);
    }

    code
}

pub fn encode_tagged_type(
    member: &impl Member,
    namespace: &str,
    param: &str,
    is_data_member: bool,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let data_type = member.data_type();

    assert!(data_type.is_optional && member.tag().is_some());

    let tag = member.tag().unwrap();

    let read_only_memory = match data_type.concrete_type() {
        Types::Sequence(sequence_def)
            if sequence_def.has_fixed_size_numeric_elements()
                && !is_data_member
                && !data_type.has_attribute("cs:generic", false) =>
        {
            true
        }
        _ => false,
    };

    let value = if data_type.is_value_type() {
        format!("{}.Value", param)
    } else {
        param.to_owned()
    };

    // For types with a known size, we provide a size parameter with the size of the tagged
    // param/member:
    let (size_parameter, count_value) = match data_type.concrete_type() {
        Types::Primitive(primitive_def) if primitive_def.is_fixed_size() => {
            (Some(primitive_def.min_wire_size().to_string()), None)
        }
        Types::Primitive(primitive_def) if !matches!(primitive_def, Primitive::String) => {
            if primitive_def.is_unsigned_numeric() {
                (
                    Some(format!("IceEncoder.GetVarULongEncodedSize({})", value)),
                    None,
                )
            } else {
                (
                    Some(format!("IceEncoder.GetVarLongEncodedSize({})", value)),
                    None,
                )
            }
        }
        Types::Struct(struct_def) if struct_def.is_fixed_size() => {
            (Some(struct_def.min_wire_size().to_string()), None)
        }
        Types::Enum(enum_def) => {
            if let Some(underlying) = &enum_def.underlying {
                (Some(underlying.min_wire_size().to_string()), None)
            } else {
                (Some(format!("encoder.GetSizeLength((int){})", value)), None)
            }
        }
        Types::Sequence(sequence_def)
            if sequence_def.element_type.is_fixed_size()
                && !sequence_def.element_type.is_optional =>
        {
            if read_only_memory {
                (
                    Some(format!(
                        "encoder.GetSizeLength({value}.Length) + {element_min_wire_size} * {value}.Length",
                        value = value,
                        element_min_wire_size = sequence_def.element_type.min_wire_size()
                    )),
                    None,
                )
            } else {
                (
                    Some(format!(
                        "encoder.GetSizeLength(count) + {} * count",
                        sequence_def.element_type.min_wire_size()
                    )),
                    Some(value.clone()),
                )
            }
        }
        Types::Dictionary(dictionary_def)
            if dictionary_def.key_type.is_fixed_size()
                && !dictionary_def.key_type.is_optional
                && dictionary_def.value_type.is_fixed_size()
                && !dictionary_def.value_type.is_optional =>
        {
            (
                Some(format!(
                    "encoder.GetSizeLength(count) + {min_wire_size} * count",
                    min_wire_size = dictionary_def.key_type.min_wire_size()
                        + dictionary_def.value_type.min_wire_size()
                )),
                Some(value.clone()),
            )
        }
        _ => (None, None),
    };

    let mut args = vec![];
    args.push(tag.to_string());

    args.push(format!("IceRpc.Slice.TagFormat.{}", data_type.tag_format()));
    if let Some(size_parameter) = size_parameter {
        args.push("size: ".to_owned() + &size_parameter);
    }
    args.push(value);
    args.push(
        encode_action(
            &clone_as_non_optional(data_type),
            namespace,
            !is_data_member,
            !is_data_member,
        )
        .to_string(),
    );

    writeln!(
        code,
        "\
if ({param} != null)
{{
    {encode}
}}",
        param = if read_only_memory {
            param.to_owned() + ".Span"
        } else {
            param.to_owned()
        },
        encode = {
            let mut code = CodeBlock::new();
            if let Some(value) = count_value {
                writeln!(code, "int count = {}.Count();", value);
            }
            writeln!(code, "encoder.EncodeTagged({});", args.join(", "));
            code
        }
        .indent()
    );

    code
}

pub fn encode_sequence(
    sequence_ref: &TypeRef<Sequence>,
    namespace: &str,
    value: &str,
    is_param: bool,
    is_read_only: bool,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    let has_custom_type = matches!(sequence_ref.get_attribute("cs:generic", false), Some(_));
    let mut args = Vec::new();

    let mut with_bit_sequence = "";

    if sequence_ref.has_fixed_size_numeric_elements() && (is_read_only || !has_custom_type) {
        if is_param && is_read_only && !has_custom_type {
            args.push(format!("{}.Span", value));
        } else {
            args.push(value.to_owned());
        }
    } else {
        args.push(value.to_owned());

        if sequence_ref.element_type.is_bit_sequence_encodable() {
            with_bit_sequence = "WithBitSequence";
        }

        args.push(
            encode_action(
                &sequence_ref.element_type,
                namespace,
                is_read_only,
                is_param,
            )
            .to_string(),
        );
    }

    write!(
        code,
        "encoder.EncodeSequence{with_bit_sequence}({args})",
        args = args.join(", "),
        with_bit_sequence = with_bit_sequence
    );

    code
}

pub fn encode_dictionary(dictionary_def: &Dictionary, namespace: &str, param: &str) -> CodeBlock {
    let mut code = CodeBlock::new();

    let mut args = vec![param.to_owned()];
    args.push(encode_action(&dictionary_def.key_type, namespace, false, false).to_string());
    args.push(encode_action(&dictionary_def.value_type, namespace, false, false).to_string());

    let with_bit_sequence = dictionary_def.value_type.is_bit_sequence_encodable();
    write!(
        code,
        "encoder.{method}({args})",
        method = if with_bit_sequence && dictionary_def.value_type.is_optional {
            "EncodeDictionaryWithBitSequence"
        } else {
            "EncodeDictionary"
        },
        args = args.join(", ")
    );

    code
}

pub fn encode_as_optional(
    type_ref: &TypeRef,
    bit_sequence_index: &mut i64,
    for_nested_type: bool,
    param: &str,
    encode_type: &CodeBlock,
) -> CodeBlock {
    let mut code = CodeBlock::new();

    match type_ref.concrete_type() {
        Types::Interface(_) => {
            writeln!(code, "encoder.EncodeNullableProxy({}?.Proxy);", param)
        }
        Types::Class(_) => {
            writeln!(code, "encoder.EncodeNullableClass({});", param)
        }
        Types::Primitive(primitive) if matches!(primitive, Primitive::AnyClass) => {
            writeln!(code, "encoder.EncodeNullableClass({});", param)
        }
        _ => {
            assert!(*bit_sequence_index >= 0);
            // A null T[]? or List<T>? is implicitly converted into a default aka null
            // ReadOnlyMemory<T> or ReadOnlySpan<T>. Furthermore, the span of a default
            // ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which is distinct from
            // the span of an empty sequence. This is why the "value.Span != null" below
            // works correctly.
            writeln!(
                code,
                "\
if ({param} != null)
{{
    {encode_type}
}}
else
{{
    bitSequence[{bit_sequence_index}] = false;
}}
",
                param = match type_ref.concrete_type() {
                    Types::Sequence(sequence_def)
                        if sequence_def.has_fixed_size_numeric_elements()
                            && !type_ref.has_attribute("cs:generic", false)
                            && !for_nested_type =>
                        format!("{}.Span", param),
                    _ => param.to_owned(),
                },
                encode_type = encode_type,
                bit_sequence_index = *bit_sequence_index
            );
            *bit_sequence_index += 1;
        }
    }

    code
}

pub fn encode_action(
    type_ref: &TypeRef,
    namespace: &str,
    is_read_only: bool,
    is_param: bool,
) -> CodeBlock {
    let mut code = CodeBlock::new();
    let is_optional = type_ref.is_optional;

    let value = match (type_ref.is_optional, type_ref.is_value_type()) {
        (true, false) => "value!",
        (true, true) => "value!.Value",
        _ => "value",
    };

    match &type_ref.concrete_typeref() {
        TypeRefs::Interface(_) => {
            if is_optional {
                write!(
                    code,
                    "(encoder, value) => encoder.EncodeNullableProxy(value?.Proxy)"
                );
            } else {
                write!(code, "(encoder, value) => encoder.EncodeProxy(value.Proxy)");
            }
        }
        TypeRefs::Class(_) => {
            if is_optional {
                write!(
                    code,
                    "(encoder, value) => encoder.EncodeNullableClass(value)"
                );
            } else {
                write!(code, "(encoder, value) => encoder.EncodeClass(value)");
            }
        }
        TypeRefs::Primitive(primitive_ref) => {
            write!(
                code,
                "(encoder, value) => encoder.Encode{builtin_type}({value})",
                builtin_type = primitive_ref.type_suffix(),
                value = value
            )
        }
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "(encoder, value) => {helper}.Encode{name}(encoder, {value})",
                helper = enum_ref.helper_name(namespace),
                name = enum_ref.identifier(),
                value = value
            )
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            write!(
                code,
                "(encoder, value) => {}",
                encode_dictionary(dictionary_ref, namespace, "value")
            );
        }
        TypeRefs::Sequence(sequence_ref) => {
            // We generate the sequence encoder inline, so this function must not be called when
            // the top-level object is not cached.
            write!(
                code,
                "(encoder, value) => {}",
                encode_sequence(sequence_ref, namespace, "value", is_read_only, is_param)
            )
        }
        TypeRefs::Struct(_) => {
            write!(code, "(encoder, value) => {}.Encode(encoder)", value)
        }
    }

    code
}

pub fn encode_operation(operation: &Operation, return_type: bool) -> CodeBlock {
    let mut code = CodeBlock::new();
    let namespace = &operation.namespace();

    let members = if return_type {
        operation.nonstreamed_return_members()
    } else {
        operation.nonstreamed_parameters()
    };

    let (required_members, tagged_members) = get_sorted_members(&members);

    let mut bit_sequence_index: i64 = -1;

    let bit_sequence_size = get_bit_sequence_size(&members);

    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequence = encoder.EncodeBitSequence({});",
            bit_sequence_size
        );
        bit_sequence_index = 0;
    }

    for member in required_members {
        code.writeln(&encode_type(
            member.data_type(),
            &mut bit_sequence_index,
            false,
            namespace,
            &match members.as_slice() {
                [_] => "value".to_owned(),
                _ => format!("value.{}", &member.field_name(FieldType::NonMangled)),
            },
        ));
    }

    if bit_sequence_size > 0 {
        assert_eq!(bit_sequence_index, bit_sequence_size as i64);
    }

    for member in tagged_members {
        code.writeln(&encode_tagged_type(
            member,
            namespace,
            &match members.as_slice() {
                [_] => "value".to_owned(),
                _ => format!("value.{}", &member.field_name(FieldType::NonMangled)),
            },
            false,
        ));
    }

    code
}
