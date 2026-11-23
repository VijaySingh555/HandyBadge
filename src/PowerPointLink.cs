using System;
using System.Runtime.InteropServices;

namespace HandyBadgeRemote
{
    // Reads the running PowerPoint slideshow through COM (desktop PowerPoint only). Never keeps PowerPoint objects alive.
    class PowerPointLink
    {
        public bool Available, Presenting;
        public int Slide, Total;
        public string NextTitle = "";
        DateTime lastQuery = DateTime.MinValue;

        static dynamic App() { return Marshal.GetActiveObject("PowerPoint.Application"); }

        public void Refresh(bool force = false)
        {
            if (!force && (DateTime.Now - lastQuery).TotalMilliseconds < 500) return;
            lastQuery = DateTime.Now;
            try
            {
                dynamic app = App();
                Available = true;
                if ((int)app.SlideShowWindows.Count < 1) { Presenting = false; return; }
                dynamic win = app.SlideShowWindows.Item(1);
                dynamic pres = win.Presentation;
                Total = (int)pres.Slides.Count;
                Slide = (int)win.View.CurrentShowPosition;
                Presenting = true;
                NextTitle = TitleOf(pres, CurrentIndex(win) + 1, Total);
            }
            catch (Exception)
            {
                Available = false; Presenting = false;
            }
        }

        static int CurrentIndex(dynamic win)
        {
            try { return (int)win.View.Slide.SlideIndex; } catch { return int.MaxValue - 1; } // e.g. the black end slide
        }

        static string TitleOf(dynamic pres, int idx, int total)
        {
            if (idx > total) return "End of slideshow";
            try
            {
                dynamic slide = pres.Slides.Item(idx);
                if ((int)slide.Shapes.HasTitle != 0)
                {
                    string t = slide.Shapes.Title.TextFrame.TextRange.Text;
                    t = (t ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\v', ' ').Trim();
                    if (t.Length > 0) return t;
                }
                return "Slide " + idx;
            }
            catch { return "Slide " + idx; }
        }

        // Advances the slideshow directly, so it works even when the slideshow isn't the focused window.
        public bool Next()
        {
            try { App().SlideShowWindows.Item(1).View.Next(); lastQuery = DateTime.MinValue; return true; }
            catch (Exception e) { Log.Write("PowerPoint Next failed: " + e.Message); return false; }
        }
    }
}
