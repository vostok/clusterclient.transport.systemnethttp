using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.Contents
{
    internal class EmptyContent : HttpContent
    {
        public EmptyContent()
        {
            Headers.ContentLength = 0;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            => Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult(Stream.Null);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return true;
        }
    }
}
