using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Emit;
using Vostok.Clusterclient.Core.Model;
using Vostok.Commons.Environment;
using Vostok.Logging.Abstractions;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.Header
{
    internal static class RequestHeadersUnlocker
    {
        private static readonly object Sync = new object();
        private static readonly Action<HttpHeaders> Empty = delegate {};
        private static volatile Action<HttpHeaders> unlocker;

        public static bool TryUnlockRestrictedHeaders(HttpHeaders headers, ILog log)
        {
            EnsureInitialized(log);

            return TryUnlockRestrictedHeadersInternal(headers, log);
        }

        private static void EnsureInitialized(ILog log)
        {
            if (unlocker != null)
                return;

            lock (Sync)
            {
                if (unlocker == null)
                {
                    var unlockAction = BuildUnlocker(log);
                    if (Test(unlockAction, log))
                    {
                        unlocker = unlockAction;
                        return;
                    }
                }

                unlocker = Empty;
            }
        }

        private static bool TryUnlockRestrictedHeadersInternal(HttpHeaders headers, ILog log)
        {
            if (ReferenceEquals(unlocker, Empty))
                return false;

            try
            {
                unlocker(headers);
                return true;
            }
            catch (Exception error)
            {
                if (unlocker != Empty)
                    log.ForContext(typeof(RequestHeadersUnlocker)).Warn(error, "Failed to unlock HttpHeaders for unsafe assignment.");

                unlocker = Empty;
                return false;
            }
        }

        private static Action<HttpHeaders> BuildUnlocker(ILog log)
        {
            try
            {
                if (RuntimeDetector.IsDotNetCore21AndNewer)
                {
                    var allowLambda = CreateAssignment<HttpHeaders>("_allowedHeaderTypes", BindingFlags.Instance | BindingFlags.NonPublic, (int)HeaderType.Custom);
                    var treatLambda = CreateAssignment<HttpHeaders>("_treatAsCustomHeaderTypes", BindingFlags.Instance | BindingFlags.NonPublic, (int)HeaderType.All);

                    return h =>
                    {
                        allowLambda(h);
                        treatLambda(h);
                    };
                }

                return CreateNullAssignment<HttpHeaders>("_invalidHeaders", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            catch (Exception error)
            {
                log.ForContext(typeof(RequestHeadersUnlocker)).Warn(error, "Could not unlock HttpHeaders.");
                return Empty;
            }
        }

        private static Action<TType> CreateAssignment<TType>(string field, BindingFlags bindingFlags, int value)
        {
            var type = typeof(TType);

            var fieldInfo = type.GetField(field, bindingFlags);

            var dyn = new DynamicMethod($"Assign_{field}_{value}", null, new[] {typeof(TType)}, typeof(RequestHeadersUnlocker));

            var il = dyn.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Stfld, fieldInfo);
            il.Emit(OpCodes.Ret);

            return (Action<TType>)dyn.CreateDelegate(typeof(Action<TType>));
        }

        private static Action<TType> CreateNullAssignment<TType>(string field, BindingFlags bindingFlags)
        {
            var type = typeof(TType);

            var fieldInfo = type.GetField(field, bindingFlags);

            var dyn = new DynamicMethod($"Assign_null_to_{field}", null, new[] {typeof(TType)}, typeof(RequestHeadersUnlocker));

            var il = dyn.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stfld, fieldInfo);
            il.Emit(OpCodes.Ret);

            return (Action<TType>)dyn.CreateDelegate(typeof(Action<TType>));
        }

        private static bool Test(Action<HttpHeaders> unlockAction, ILog log)
        {
            (string name, string value)[] tests =
            {
                (HeaderNames.AcceptEncoding, "deflate"),
                (HeaderNames.ContentRange, "bytes 200-1000/67589"),
                (HeaderNames.ContentLanguage, "mi, en"),
                (HeaderNames.Referer, "whatever")
            };

            try
            {
                using (var request = new HttpRequestMessage())
                {
                    var headers = request.Headers;
                    unlockAction(request.Headers);

                    foreach (var (name, expectedValue) in tests)
                    {
                        if (!headers.TryAddWithoutValidation(name, expectedValue) ||
                            !headers.TryGetValues(name, out var value))
                        {
                            log
                                .ForContext(typeof(RequestHeadersUnlocker))
                                .Warn($"Can't unlock HttpHeaders. Test failed on header '{name}'. Unable set header value.");

                            return false;
                        }

                        if (!string.Equals(value.FirstOrDefault(), expectedValue, StringComparison.Ordinal))
                        {
                            log
                                .ForContext(typeof(RequestHeadersUnlocker))
                                .Warn($"Can't unlock HttpHeaders. Test failed on header '{name}'. Expected value: '{expectedValue}', actual: '{value}'.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception error)
            {
                log.ForContext(typeof(RequestHeadersUnlocker)).Warn(error, "Failed to unlock HttpHeaders.");
                return false;
            }

            return true;
        }
    }
}
