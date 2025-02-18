// Copyright (c) ZeroC, Inc.

using IceRpc.Features;

namespace IceRpc.Extensions.DependencyInjection;

/// <summary>Provides extension methods for <see cref="IDispatcherBuilder" /> to add a middleware that sets a feature.
/// </summary>
public static class DispatcherBuilderExtensions
{
    /// <summary>Adds a middleware that creates and inserts the <see cref="IDispatchInformationFeature" /> feature
    /// in all requests.</summary>
    /// <param name="builder">The builder being configured.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseDispatchInformation(this IDispatcherBuilder builder) =>
        builder.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With<IDispatchInformationFeature>(
                new DispatchInformationFeature(request));
            return next.DispatchAsync(request, cancellationToken);
        }));

    /// <summary>Adds a middleware that sets a feature in all requests.</summary>
    /// <typeparam name="TFeature">The type of the feature.</typeparam>
    /// <param name="builder">The builder being configured.</param>
    /// <param name="feature">The value of the feature to set in all requests.</param>
    /// <returns>The builder.</returns>
    public static IDispatcherBuilder UseFeature<TFeature>(this IDispatcherBuilder builder, TFeature feature) =>
        builder.Use(next => new InlineDispatcher((request, cancellationToken) =>
        {
            request.Features = request.Features.With(feature);
            return next.DispatchAsync(request, cancellationToken);
        }));
}
