using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.Graphics.OpenGL.Effects
{
    internal static class ShaderHelper
    {
        public static int CompileProgram(string shaderCode, ShaderType shaderType)
        {
            var shader = GL.CreateShader(shaderType);
            GL.ShaderSource(shader, shaderCode);
            GL.CompileShader(shader);

            var program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);

            GL.DetachShader(program, shader);
            GL.DeleteShader(shader);

            return program;
        }

        public unsafe static int CompileProgram(string[] shaders, ShaderType shaderType)
        {
            var shader = GL.CreateShader(shaderType);
            GL.ShaderSource(shader, 2, shaders, (int*)IntPtr.Zero);
            GL.CompileShader(shader);

            var program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);

            GL.DetachShader(program, shader);
            GL.DeleteShader(shader);

            return program;
        }
    }
}
