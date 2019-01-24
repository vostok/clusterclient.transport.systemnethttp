using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core.Model;

namespace Vostok.Clusterclient.Transport.SystemNetHttp.Contents
{
    internal class BufferContent : GenericContent
    {
        private readonly Content content;
        private readonly CancellationToken cancellationToken;

        public BufferContent(Content content, CancellationToken cancellationToken)
        {
            this.content = content;
            this.cancellationToken = cancellationToken;

            Headers.ContentLength = content.Length;
        }

        public override long? Length => content.Length;

        public override Stream AsStream => content.ToMemoryStream();

        public override Task Copy(Stream target) => target.WriteAsync(content.Buffer, content.Offset, content.Length, cancellationToken);
    }
}
