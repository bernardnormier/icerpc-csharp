// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;

namespace IceRpc.Configure
{
    /// <summary>This class provide extension methods to add built-in middleware to a <see cref="Router"/>
    /// </summary>
    public static class RouterExtensions
    {
        /// <summary>Adds a middleware that sets a feature in all requests.</summary>
        /// <paramtype name="T">The type of the feature.</paramtype>
        /// <param name="router">The router being configured.</param>
        /// <param name="feature">The value of the feature to set in all requests.</param>
        public static Router UseFeature<T>(this Router router, T feature) =>
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                request.Features = request.Features.With(feature);
                return next.DispatchAsync(request, cancel);
            }));
    }
}
