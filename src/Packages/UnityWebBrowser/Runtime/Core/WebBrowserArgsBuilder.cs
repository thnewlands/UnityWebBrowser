using System.Collections.Generic;

namespace UnityWebBrowser.Core
{
    /// <summary>
    ///     Creates a <see cref="string" />
    /// </summary>
    internal class WebBrowserArgsBuilder
    {
        private readonly List<string> arguments;

        internal WebBrowserArgsBuilder()
        {
            arguments = new List<string>();
        }

        /// <summary>
        ///     Adds an argument
        /// </summary>
        /// <param name="arg"></param>
        /// <param name="parameters"></param>
        /// <param name="quotes"></param>
        public void AppendArgument(string arg, object parameters = null, bool quotes = false)
        {
            string builtArg = $"-{arg}";
            if (parameters == null || string.IsNullOrEmpty(parameters.ToString()))
                return;

            //We got spaces
            if (quotes)
                builtArg += $" \"{parameters}\"";
            else
                builtArg += $" {parameters}";

            arguments.Add(builtArg);
        }

        /// <summary>
        ///     Gets the joined arguments <see cref="string" />
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Join(" ", arguments);
        }
    }
}