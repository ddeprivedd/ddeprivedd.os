using System;

namespace ddeprivedd.os
{
    public enum LogKind { Info, Success, Warning, Error, Protected }

    /// <summary>One progress report from an engine: optional percent (-1 = unchanged) and optional log line.</summary>
    public sealed class StatusUpdate
    {
        public int Percent { get; private set; }
        public string Message { get; private set; }
        public LogKind Kind { get; private set; }

        public StatusUpdate(int percent, string message, LogKind kind)
        {
            Percent = percent;
            Message = message;
            Kind = kind;
        }

        public static void Send(IProgress<StatusUpdate> progress, int percent, string message, LogKind kind = LogKind.Info)
        {
            if (progress != null) progress.Report(new StatusUpdate(percent, message, kind));
        }
    }
}
