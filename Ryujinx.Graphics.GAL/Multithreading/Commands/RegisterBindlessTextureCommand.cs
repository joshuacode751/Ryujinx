using Ryujinx.Graphics.GAL.Multithreading.Model;
using Ryujinx.Graphics.GAL.Multithreading.Resources;

namespace Ryujinx.Graphics.GAL.Multithreading.Commands
{
    struct RegisterBindlessTextureCommand : IGALCommand, IGALCommand<RegisterBindlessTextureCommand>
    {
        public CommandType CommandType => CommandType.RegisterBindlessTexture;
        private int _textureId;
        private TableRef<ITexture> _texture;

        public void Set(int textureId, TableRef<ITexture> texture)
        {
            _textureId = textureId;
            _texture = texture;
        }

        public static void Run(ref RegisterBindlessTextureCommand command, ThreadedRenderer threaded, IRenderer renderer)
        {
            renderer.Pipeline.RegisterBindlessTexture(command._textureId, command._texture.GetAs<ThreadedTexture>(threaded)?.Base);
        }
    }
}
