using System.Windows.Forms;

namespace ZVClusterApp.WinForms
{
    internal class FlickerFreeListView : ListView
    {
        public FlickerFreeListView()
        {
            // Reduce flicker when adding/removing items frequently
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.EnableNotifyMessage, true);
            DoubleBuffered = true;
        }

        protected override void OnNotifyMessage(Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (m.Msg == WM_ERASEBKGND) return; // ignore background erase
            base.OnNotifyMessage(m);
        }
    }
}
