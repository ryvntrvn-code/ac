// Atheo Session Times
//
// Add it in cTrader: Automate > New Indicator > paste this file > Build > attach it to a chart.
// Session hours and minutes are UTC.

using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;

namespace cAlgo
{
    // Overlay boxes for the Asian, London, and New York sessions.
    // Bar times and drawing times use the indicator clock (UTC): Bars.OpenTimes and Server.Time.
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AtheoSessionTimes : Indicator
    {
        private const string ObjectPrefix = "ATS_";

        [Parameter("Asia Enabled", DefaultValue = true, Group = "Asia")]
        public bool AsiaEnabled { get; set; }

        [Parameter("Asia Start Hour (UTC)", DefaultValue = 23, MinValue = 0, MaxValue = 23, Step = 1, Group = "Asia")]
        public int AsiaStartHour { get; set; }

        [Parameter("Asia Start Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "Asia")]
        public int AsiaStartMinute { get; set; }

        [Parameter("Asia End Hour (UTC)", DefaultValue = 8, MinValue = 0, MaxValue = 23, Step = 1, Group = "Asia")]
        public int AsiaEndHour { get; set; }

        [Parameter("Asia End Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "Asia")]
        public int AsiaEndMinute { get; set; }

        [Parameter("Asia Colour", DefaultValue = "DodgerBlue", Group = "Asia")]
        public Color AsiaColour { get; set; }

        [Parameter("London Enabled", DefaultValue = true, Group = "London")]
        public bool LondonEnabled { get; set; }

        [Parameter("London Start Hour (UTC)", DefaultValue = 8, MinValue = 0, MaxValue = 23, Step = 1, Group = "London")]
        public int LondonStartHour { get; set; }

        [Parameter("London Start Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "London")]
        public int LondonStartMinute { get; set; }

        [Parameter("London End Hour (UTC)", DefaultValue = 16, MinValue = 0, MaxValue = 23, Step = 1, Group = "London")]
        public int LondonEndHour { get; set; }

        [Parameter("London End Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "London")]
        public int LondonEndMinute { get; set; }

        [Parameter("London Colour", DefaultValue = "OrangeRed", Group = "London")]
        public Color LondonColour { get; set; }

        [Parameter("New York Enabled", DefaultValue = true, Group = "New York")]
        public bool NewYorkEnabled { get; set; }

        [Parameter("New York Start Hour (UTC)", DefaultValue = 13, MinValue = 0, MaxValue = 23, Step = 1, Group = "New York")]
        public int NewYorkStartHour { get; set; }

        [Parameter("New York Start Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "New York")]
        public int NewYorkStartMinute { get; set; }

        [Parameter("New York End Hour (UTC)", DefaultValue = 21, MinValue = 0, MaxValue = 23, Step = 1, Group = "New York")]
        public int NewYorkEndHour { get; set; }

        [Parameter("New York End Minute (UTC)", DefaultValue = 0, MinValue = 0, MaxValue = 59, Step = 1, Group = "New York")]
        public int NewYorkEndMinute { get; set; }

        [Parameter("New York Colour", DefaultValue = "LimeGreen", Group = "New York")]
        public Color NewYorkColour { get; set; }

        // 0 = solid fill, 100 = invisible fill. The border stays solid either way.
        [Parameter("Box Transparency", DefaultValue = 80, MinValue = 0, MaxValue = 100, Step = 1, Group = "Style")]
        public int BoxTransparency { get; set; }

        [Parameter("Border Thickness", DefaultValue = 1, MinValue = 1, MaxValue = 5, Step = 1, Group = "Style")]
        public int BorderThickness { get; set; }

        private readonly Dictionary<string, SessionBox> _boxes = new Dictionary<string, SessionBox>();

        // Index of the bar last treated as still forming, or -1. When that bar closes,
        // its end becomes the next bar's open (so a weekend gap is included) and it is accumulated once more.
        private int _formingBarIndex = -1;

        public override void Calculate(int index)
        {
            if (index < 0 || index >= Bars.Count)
                return;

            // Full pass on start and on any parameter change. Tick and new-bar updates call Calculate(last) only.
            if (index == 0)
            {
                RemoveOwnObjects();
                _boxes.Clear();
                _formingBarIndex = -1;
            }

            if (_formingBarIndex >= 0 && index != _formingBarIndex && _formingBarIndex < Bars.Count)
                AccumulateBar(_formingBarIndex);

            AccumulateBar(index);
            _formingBarIndex = index == Bars.Count - 1 ? index : -1;
        }

        private void AccumulateBar(int index)
        {
            DateTime barStart = Bars.OpenTimes[index];
            DateTime barEnd = BarEnd(index);
            if (barEnd <= barStart)
                return;

            double high = Bars.HighPrices[index];
            double low = Bars.LowPrices[index];
            if (!IsRealPrice(high) || !IsRealPrice(low))
                return;

            if (high < low)
            {
                double swap = high;
                high = low;
                low = swap;
            }

            if (AsiaEnabled)
                AccumulateSession("Asia", AsiaStartHour, AsiaStartMinute, AsiaEndHour, AsiaEndMinute, AsiaColour, barStart, barEnd, high, low);

            if (LondonEnabled)
                AccumulateSession("London", LondonStartHour, LondonStartMinute, LondonEndHour, LondonEndMinute, LondonColour, barStart, barEnd, high, low);

            if (NewYorkEnabled)
                AccumulateSession("NewYork", NewYorkStartHour, NewYorkStartMinute, NewYorkEndHour, NewYorkEndMinute, NewYorkColour, barStart, barEnd, high, low);
        }

        // A bar overlaps a session when [barStart, barEnd) overlaps [sessionStart, sessionEnd).
        // The whole bar high/low is used; a higher-timeframe bar can update several occurrences.
        // Each occurrence is keyed by session name + session start so they stay separate boxes.
        private void AccumulateSession(
            string sessionName,
            int startHour,
            int startMinute,
            int endHour,
            int endMinute,
            Color colour,
            DateTime barStart,
            DateTime barEnd,
            double high,
            double low)
        {
            startHour = Clamp(startHour, 0, 23);
            startMinute = Clamp(startMinute, 0, 59);
            endHour = Clamp(endHour, 0, 23);
            endMinute = Clamp(endMinute, 0, 59);

            int startOfDay = startHour * 60 + startMinute;
            int endOfDay = endHour * 60 + endMinute;

            // Equal clock times are an empty session. An earlier end finishes the next day.
            if (startOfDay == endOfDay)
                return;

            int durationMinutes = endOfDay > startOfDay
                ? endOfDay - startOfDay
                : (24 * 60 - startOfDay) + endOfDay;
            TimeSpan duration = TimeSpan.FromMinutes(durationMinutes);

            DateTime earliestExclusive = barStart - duration;
            DateTime firstDay = earliestExclusive.Date.AddDays(-1);
            DateTime lastDay = barEnd.Date.AddDays(1);

            for (DateTime day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                DateTime sessionStart = new DateTime(day.Year, day.Month, day.Day, startHour, startMinute, 0, barStart.Kind);
                if (sessionStart <= earliestExclusive || sessionStart >= barEnd)
                    continue;

                DateTime sessionEnd = sessionStart + duration;
                if (sessionEnd <= barStart)
                    continue;

                ExpandBox(sessionName, sessionStart, sessionEnd, colour, high, low);
            }
        }

        private void ExpandBox(string sessionName, DateTime sessionStart, DateTime sessionEnd, Color colour, double high, double low)
        {
            string key = sessionName + "_" + sessionStart.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

            SessionBox box;
            if (_boxes.TryGetValue(key, out box))
            {
                double nextHigh = Math.Max(box.High, high);
                double nextLow = Math.Min(box.Low, low);
                if (nextHigh == box.High && nextLow == box.Low)
                    return;

                box.High = nextHigh;
                box.Low = nextLow;
            }
            else
            {
                box = new SessionBox();
                box.Key = key;
                box.Start = sessionStart;
                box.End = sessionEnd;
                box.High = high;
                box.Low = low;
                _boxes.Add(key, box);
            }

            DrawBox(box, colour);
        }

        private void DrawBox(SessionBox box, Color colour)
        {
            string fillName = ObjectPrefix + box.Key;
            string borderName = fillName + "_b";
            int thickness = Clamp(BorderThickness, 1, 5);
            Color fill = Color.FromArgb(FillAlpha(), colour);

            ChartRectangle area = Chart.DrawRectangle(fillName, box.Start, box.High, box.End, box.Low, fill);
            area.IsFilled = true;
            area.Thickness = thickness;
            area.IsInteractive = false;
            area.ZIndex = 0;

            ChartRectangle border = Chart.DrawRectangle(borderName, box.Start, box.High, box.End, box.Low, colour);
            border.IsFilled = false;
            border.Thickness = thickness;
            border.IsInteractive = false;
            border.ZIndex = 1;
        }

        private int FillAlpha()
        {
            int transparency = Clamp(BoxTransparency, 0, 100);
            int alpha = (int)Math.Round((100 - transparency) / 100.0 * 255.0, MidpointRounding.AwayFromZero);
            return Clamp(alpha, 0, 255);
        }

        // Closed bars end at the next bar's open. The forming bar ends one chart period later.
        private DateTime BarEnd(int index)
        {
            if (index < Bars.Count - 1)
                return Bars.OpenTimes[index + 1];

            return FormingBarEnd(Bars.OpenTimes[index]);
        }

        private DateTime FormingBarEnd(DateTime open)
        {
            DateTime end;
            if (TryPeriodEnd(Bars.TimeFrame.Name, open, out end))
                return end;

            if (TryPeriodEnd(Bars.TimeFrame.ShortName, open, out end))
                return end;

            string text = Bars.TimeFrame.ToString();
            if (text != Bars.TimeFrame.Name && text != Bars.TimeFrame.ShortName && TryPeriodEnd(text, open, out end))
                return end;

            // Tick, range, and renko bars have no fixed period. Keep the forming bar on the indicator clock.
            DateTime now = Server.Time;
            return now > open ? now : open.AddTicks(1);
        }

        // Long names: Minute15, Hour4, Daily, Day2, Weekly, Monthly, HeikinHour4.
        // Short names: m15, h4, D1, W1, Month1, Hm15, Hh4, HD1, HW1, HMonth1.
        private static bool TryPeriodEnd(string token, DateTime open, out DateTime end)
        {
            end = open;
            if (string.IsNullOrEmpty(token))
                return false;

            if (token.StartsWith("Heikin", StringComparison.Ordinal))
                token = token.Substring("Heikin".Length);

            int count;
            if (TryCount(token, "Minute", out count))
            {
                end = open.AddMinutes(count);
                return true;
            }

            if (TryCount(token, "Hour", out count))
            {
                end = open.AddHours(count);
                return true;
            }

            if (token == "Daily")
            {
                end = open.AddDays(1);
                return true;
            }

            if (TryCount(token, "Day", out count))
            {
                end = open.AddDays(count);
                return true;
            }

            if (token == "Weekly")
            {
                end = open.AddDays(7);
                return true;
            }

            if (token == "Monthly")
            {
                end = open.AddMonths(1);
                return true;
            }

            if (TryCount(token, "HMonth", out count))
            {
                end = open.AddMonths(count);
                return true;
            }

            if (TryCount(token, "Month", out count))
            {
                end = open.AddMonths(count);
                return true;
            }

            if (TryCount(token, "Hh", out count))
            {
                end = open.AddHours(count);
                return true;
            }

            if (TryCount(token, "Hm", out count))
            {
                end = open.AddMinutes(count);
                return true;
            }

            if (TryCount(token, "HD", out count))
            {
                end = open.AddDays(count);
                return true;
            }

            if (TryCount(token, "HW", out count))
            {
                end = open.AddDays(7 * count);
                return true;
            }

            if (TryCount(token, "m", out count))
            {
                end = open.AddMinutes(count);
                return true;
            }

            if (TryCount(token, "h", out count))
            {
                end = open.AddHours(count);
                return true;
            }

            if (TryCount(token, "D", out count))
            {
                end = open.AddDays(count);
                return true;
            }

            if (TryCount(token, "W", out count))
            {
                end = open.AddDays(7 * count);
                return true;
            }

            return false;
        }

        private static bool TryCount(string token, string prefix, out int count)
        {
            count = 0;
            if (!token.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            if (token.Length == prefix.Length)
            {
                count = 1;
                return true;
            }

            int value;
            if (!int.TryParse(token.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value) || value <= 0)
                return false;

            count = value;
            return true;
        }

        // Only objects this indicator created. Never Chart.RemoveAllObjects.
        private void RemoveOwnObjects()
        {
            List<string> names = new List<string>();
            foreach (ChartObject chartObject in Chart.Objects)
            {
                if (chartObject.Name != null && chartObject.Name.StartsWith(ObjectPrefix, StringComparison.Ordinal))
                    names.Add(chartObject.Name);
            }

            for (int i = 0; i < names.Count; i++)
                Chart.RemoveObject(names[i]);
        }

        private static bool IsRealPrice(double price)
        {
            return !double.IsNaN(price) && !double.IsInfinity(price);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private sealed class SessionBox
        {
            public string Key;
            public DateTime Start;
            public DateTime End;
            public double High;
            public double Low;
        }
    }
}
