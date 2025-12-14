using System;

namespace ZVClusterApp.WinForms
{
    public interface IRadioDriver : IDisposable
    {
        bool Enabled { get; set; }
        string Port { get; set; }
        int Baud { get; set; }

        // New: identity and model selection
        // Manufacturer is provided by the concrete driver family (e.g., "Icom", "Yaesu", "Kenwood").
        // ModelId selects the specific rig/model so the driver can choose the correct CAT/CIV profile.
        string Manufacturer { get; }
        string ModelId { get; set; }

        bool Connect();
        void Disconnect();
        bool SetFrequencyAndMode(int frequencyHz, string? mode);
    }
}
