using System.Collections.Generic;
using System.Linq;

namespace HandyBadgeRemote
{
    enum SwipeResult { None, Left, Right, Unknown, Calibrating, Calibrated, CalibrationFailed, Learned, Relearning }

    // Same logic as HandyBadge v0.12. The ring holds image footprints in swipe-right order.
    // Calibration: two left swipes give values a, b, c in left order, so the ring is c, b, a.
    class Detector
    {
        public List<int> Ring = new List<int>();
        public bool Calibrated;
        public bool CalibrationActive { get; private set; }
        readonly List<int> calSeq = new List<int>();
        int prevFrom, prevTo;

        public void StartCalibration() { CalibrationActive = true; calSeq.Clear(); }
        public void CancelCalibration() { CalibrationActive = false; calSeq.Clear(); }

        public bool Flip()
        {
            if (Ring.Count < 3 || !Calibrated) return false;
            Ring.Reverse();
            return true;
        }

        public int IndexOf(int v) { return Ring.IndexOf(v); }

        // Call when the first value after connecting is seen.
        public bool CheckFirst(int v)
        {
            if (Ring.Count > 0 && !Ring.Contains(v)) { Ring.Clear(); Calibrated = false; return true; }
            return false;
        }

        public SwipeResult Transition(int from, int to)
        {
            if (CalibrationActive)
            {
                if (calSeq.Count == 0) calSeq.Add(from);
                calSeq.Add(to);
                if (calSeq.Count < 3) return SwipeResult.Calibrating;
                int a = calSeq[0], b = calSeq[1], c = calSeq[2];
                CancelCalibration();
                if (a == c || new HashSet<int> { a, b, c }.Count < 3) return SwipeResult.CalibrationFailed;
                Ring = new List<int> { c, b, a };
                Calibrated = true;
                prevFrom = from; prevTo = to;
                return SwipeResult.Calibrated;
            }

            var result = SwipeResult.None;
            if (Ring.Count > 0 && (!Ring.Contains(to) || !Ring.Contains(from)))
            {
                Ring.Clear(); Calibrated = false; result = SwipeResult.Relearning;
                prevFrom = 0; prevTo = 0; // start learning afresh
            }

            if (Ring.Count == 0)
            {
                if (prevTo == from && prevFrom != 0 && prevFrom != to && new HashSet<int> { prevFrom, from, to }.Count == 3)
                {
                    Ring = new List<int> { prevFrom, from, to };
                    Calibrated = false;
                    result = SwipeResult.Learned;
                }
                prevFrom = from; prevTo = to;
                return result == SwipeResult.None ? SwipeResult.Unknown : result;
            }

            prevFrom = from; prevTo = to;
            if (!Calibrated) return SwipeResult.Unknown;
            int n = Ring.Count, d = (Ring.IndexOf(to) - Ring.IndexOf(from) + n) % n;
            if (d == 1) return SwipeResult.Right;
            if (d == n - 1) return SwipeResult.Left;
            return SwipeResult.Unknown;
        }
    }
}
