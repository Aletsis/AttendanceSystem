using System.Globalization;
using AttendanceSystem.Application.DTOs;
using AttendanceSystem.Domain.Enumerations;

namespace AttendanceSystem.Application.Common;

public static class WorkPeriodHelper
{
    public static (DateTime Start, DateTime End) GetPeriodDates(this SystemConfigurationDto config, int year, int periodNum)
    {
        if (config.WorkPeriodMode == WorkPeriodMode.Weekly)
        {
            try 
            {
                var s = ISOWeek.ToDateTime(year, periodNum, config.WeeklyStartDay);
                return (s, s.AddDays(6));
            }
            catch
            {
                 // Fallback or retry logic from original code
                 var s = ISOWeek.ToDateTime(year, 1, config.WeeklyStartDay).AddDays((periodNum - 1) * 7);
                 return (s, s.AddDays(6));
            }
        }
        else if (config.WorkPeriodMode == WorkPeriodMode.Monthly)
        {
            if (periodNum < 1) periodNum = 1;
            if (periodNum > 12) periodNum = 12;

            int maxDay = DateTime.DaysInMonth(year, periodNum);
            int sDay = Math.Min(config.MonthlyStartDay, maxDay);
            
            var s = new DateTime(year, periodNum, sDay);
            var e = s.AddMonths(1).AddDays(-1);
            return (s, e);
        }
        else // Fortnightly
        {
            if (periodNum < 1) periodNum = 1;

            int monthIndex = (periodNum - 1) / 2 + 1; // 1-12
            bool isSecondHalf = (periodNum % 2 == 0);

            if (monthIndex > 12) monthIndex = 12;

            var d1 = config.FortnightFirstDay;
            var d2 = config.FortnightSecondDay; 

            if (!isSecondHalf) // First Half 
            {
                int maxDay = DateTime.DaysInMonth(year, monthIndex);
                int startDay = Math.Min(d1, maxDay);
                var s = new DateTime(year, monthIndex, startDay);
            
                var nextPStart = new DateTime(year, monthIndex, Math.Min(d2, maxDay));
                
                return (s, nextPStart.AddDays(-1));
            }
            else // Second Half 
            {
                int maxDay = DateTime.DaysInMonth(year, monthIndex);
                int startDay = Math.Min(d2, maxDay);
                var s = new DateTime(year, monthIndex, startDay);
                
                var nextMonth = new DateTime(year, monthIndex, 1).AddMonths(1);
                var nextStart = new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(d1, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
                
                return (s, nextStart.AddDays(-1));
            }
        }
    }
}
