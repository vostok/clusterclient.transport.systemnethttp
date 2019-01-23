using System.Collections.Generic;
using System.Net.Http;
using Vostok.Clusterclient.Core.Model;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.Header
{
    internal static class ResponseHeadersConverter
    {
        public static Headers Convert(HttpResponseMessage responseMessage)
        {
            var headers = Headers.Empty;

            if (responseMessage?.Headers != null)
            {
                foreach (var (key, value) in responseMessage.Headers)
                    headers = headers.Set(key, FlattenValue(value));
            }

            if (responseMessage?.Content?.Headers != null)
            {
                foreach (var (key, value) in responseMessage.Content.Headers)
                    headers = headers.Set(key, FlattenValue(value));
            }

            return headers;
        }

        private static string FlattenValue(IEnumerable<string> value)
        {
            if (value is IList<string> valuesList && valuesList.Count == 1)
                return valuesList[0];

            return string.Join(",", value);
        }
    }
}