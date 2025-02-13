﻿using Kros.AspNetCore.Exceptions;
using Kros.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kros.AspNetCore.Authorization
{
    /// <summary>
    /// Middleware for user authorization.
    /// </summary>
    internal class GatewayAuthorizationMiddleware
    {
        /// <summary>
        /// HttpClient name used for communication between ApiGateway and Authorization service.
        /// </summary>
        public const string AuthorizationHttpClientName = "JwtAuthorizationClientName";

        private readonly RequestDelegate _next;
        private readonly GatewayJwtAuthorizationOptions _jwtAuthorizationOptions;

        /// <summary>
        /// Ctor.
        /// </summary>
        /// <param name="next">Next middleware.</param>
        /// <param name="jwtAuthorizationOptions">Authorization options.</param>
        public GatewayAuthorizationMiddleware(
            RequestDelegate next,
            GatewayJwtAuthorizationOptions jwtAuthorizationOptions)
        {
            _next = Check.NotNull(next, nameof(next));
            _jwtAuthorizationOptions = Check.NotNull(jwtAuthorizationOptions, nameof(jwtAuthorizationOptions));
        }

        /// <summary>
        /// HttpContext pipeline processing.
        /// </summary>
        /// <param name="httpContext">Http context.</param>
        /// <param name="httpClientFactory">Http client factory.</param>
        /// <param name="memoryCache">Cache for caching authorization token.</param>
        public async Task Invoke(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache)
        {
            string userJwt = await GetUserAuthorizationJwtAsync(httpContext, httpClientFactory, memoryCache);

            if (!string.IsNullOrEmpty(userJwt))
            {
                AddUserProfileClaimsToIdentityAndHttpHeaders(httpContext, userJwt);
            }

            await _next(httpContext);
        }

        private async Task<string> GetUserAuthorizationJwtAsync(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache)
        {
            if (httpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out StringValues value))
            {
                int key = GetKey(httpContext, value);

                if (!memoryCache.TryGetValue(key, out string jwtToken))
                {
                    jwtToken = await GetUserAuthorizationJwtAsync(httpContext, httpClientFactory, memoryCache, value, key);
                }

                return jwtToken;
            }

            return string.Empty;
        }

        private async Task<string> GetUserAuthorizationJwtAsync(
            HttpContext httpContext,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache,
            StringValues value,
            int key)
        {
            using (HttpClient client = httpClientFactory.CreateClient(AuthorizationHttpClientName))
            {
                client.DefaultRequestHeaders.Add(HeaderNames.Authorization, value.ToString());

                HttpResponseMessage response = await client.GetAsync(_jwtAuthorizationOptions.AuthorizationUrl + httpContext.Request.Path.Value);

                if (response.IsSuccessStatusCode)
                {
                    string jwtToken = await response.Content.ReadAsStringAsync();
                    SetTokenToCache(memoryCache, key, jwtToken);

                    return jwtToken;
                }
                else
                {
                    throw GetExceptionForResponse(response);
                }
            }
        }

        private static Exception GetExceptionForResponse(HttpResponseMessage response)
        {
            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Forbidden:
                    return new ResourceIsForbiddenException();
                case System.Net.HttpStatusCode.NotFound:
                    return new NotFoundException();
                case System.Net.HttpStatusCode.Unauthorized:
                    return new UnauthorizedAccessException(Properties.Resources.AuthorizationServiceForbiddenRequest);
                case System.Net.HttpStatusCode.BadRequest:
                    return new BadRequestException();
                default:
                    return new UnauthorizedAccessException(Properties.Resources.AuthorizationServiceForbiddenRequest);
            }
        }

        private void SetTokenToCache(IMemoryCache memoryCache, int key, string jwtToken)
        {
            if (_jwtAuthorizationOptions.CacheSlidingExpirationOffset != TimeSpan.Zero)
            {
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                        .SetSlidingExpiration(_jwtAuthorizationOptions.CacheSlidingExpirationOffset);

                memoryCache.Set(key, jwtToken, cacheEntryOptions);
            }
        }

        private static int GetKey(HttpContext httpContext, StringValues value)
            => HashCode.Combine(value, httpContext.Request.Path);

        private void AddUserProfileClaimsToIdentityAndHttpHeaders(HttpContext httpContext, string userJwtToken)
        {
            httpContext.Request.Headers[HeaderNames.Authorization] = $"Bearer {userJwtToken}";
        }
    }
}
