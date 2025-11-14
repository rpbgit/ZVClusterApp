using System;

namespace ZVClusterApp.WinForms
{
    public interface IRadioDriver : IDisposable
    {
        bool Enabled { get; set; }
        string Port { get; set; }
        int Baud { get; set; }

        bool Connect();
        void Disconnect();
        bool SetFrequencyAndMode(int frequencyHz, string? mode);
    }
}
