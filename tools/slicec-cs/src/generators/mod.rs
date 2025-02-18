// Copyright (c) ZeroC, Inc.

mod class_generator;
mod dispatch_generator;
mod enum_generator;
mod exception_generator;
mod proxy_generator;
mod struct_generator;

use crate::cs_options::{CsOptions, RpcProvider};
use crate::slicec_ext::ModuleExt;
use slicec::code_block::CodeBlock;
use slicec::grammar::*;
use slicec::slice_file::SliceFile;
use slicec::visitor::Visitor;

struct Generator<'a> {
    code: &'a mut CodeBlock,
}

impl Visitor for Generator<'_> {
    fn visit_file(&mut self, _: &SliceFile) {}

    fn visit_module(&mut self, _: &Module) {}

    fn visit_struct(&mut self, struct_def: &Struct) {
        self.code.add_block(&struct_generator::generate_struct(struct_def));
    }

    fn visit_class(&mut self, class_def: &Class) {
        self.code.add_block(&class_generator::generate_class(class_def));
    }

    fn visit_exception(&mut self, exception_def: &Exception) {
        self.code
            .add_block(&exception_generator::generate_exception(exception_def));
    }

    fn visit_interface(&mut self, interface_def: &Interface) {
        self.code.add_block(&proxy_generator::generate_proxy(interface_def));
        self.code
            .add_block(&dispatch_generator::generate_dispatch(interface_def));
    }

    fn visit_enum(&mut self, enum_def: &Enum) {
        self.code.add_block(&enum_generator::generate_enum(enum_def));
    }

    fn visit_operation(&mut self, _: &Operation) {}

    fn visit_custom_type(&mut self, _: &CustomType) {}

    fn visit_type_alias(&mut self, _: &TypeAlias) {}

    fn visit_field(&mut self, _: &Field) {}

    fn visit_parameter(&mut self, _: &Parameter) {}

    fn visit_enumerator(&mut self, _: &Enumerator) {}

    fn visit_type_ref(&mut self, _: &TypeRef) {}
}

pub fn generate_from_slice_file(slice_file: &SliceFile, options: &CsOptions) -> String {
    // Write the preamble at the top of the generated file.
    let mut generated_code = preamble(slice_file, options.rpc_provider);

    // If the slice file wasn't empty, generate code for its contents.
    if let Some(module_ptr) = &slice_file.module {
        // First generate the file's namespace declaration.
        let namespace = module_ptr.borrow().as_namespace();
        generated_code.add_block(&format!("namespace {namespace};"));

        // Then generate code for the user's slice definitions.
        let mut generator = Generator {
            code: &mut generated_code,
        };
        slice_file.visit_with(&mut generator);
    }

    // End the file with a trailing newline.
    generated_code.to_string() + "\n"
}

fn preamble(slice_file: &SliceFile, rpc_provider: RpcProvider) -> CodeBlock {
    format!(
        r#"// <auto-generated/>
// slicec-cs version: '{version}'
// Generated from file: '{file}.slice'

#nullable enable

#pragma warning disable CS1591 // Missing XML Comment
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment
#pragma warning disable CS0618 // Type or member is obsolete

{rpc_usings}using ZeroC.Slice;

[assembly:Slice("{file}.slice")]
"#,
        version = env!("CARGO_PKG_VERSION"),
        file = slice_file.filename,
        rpc_usings = rpc_usings(rpc_provider),
    )
    .into()
}

fn rpc_usings(rpc_provider: RpcProvider) -> &'static str {
    match rpc_provider {
        RpcProvider::None => "",
        RpcProvider::IceRpc => "using IceRpc.Slice;\n",
    }
}
