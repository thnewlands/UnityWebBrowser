﻿using System;
using UnityEngine;

namespace UnityWebBrowser.Logging
{
    /// <summary>
    ///     An <see cref="IWebBrowserLogger" /> using Unity's <see cref="ILogger" />
    /// </summary>
    public sealed class DefaultUnityWebBrowserLogger : IWebBrowserLogger
    {
        private const string LoggingTag = "[Web Browser]";

        private readonly ILogger logger;

        public DefaultUnityWebBrowserLogger()
        {
            logger = UnityEngine.Debug.unityLogger;
        }

        public void Debug(object message)
        {
            logger.Log(LogType.Log, LoggingTag, message);
        }

        public void Warn(object message)
        {
            logger.LogWarning(LoggingTag, message);
        }

        public void Error(object message)
        {
            logger.LogError(LoggingTag, message);
        }
    }
}