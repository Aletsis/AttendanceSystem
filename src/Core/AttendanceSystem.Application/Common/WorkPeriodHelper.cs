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
    
    public static List<WorkPeriodDto> GetAvailablePeriods(this SystemConfigurationDto config, int year)
    {
        var periods = new List<WorkPeriodDto>();
        var culture = new CultureInfo("es-MX");

        if (config.WorkPeriodMode == WorkPeriodMode.Weekly)
        {
             var startDayOfWeek = config.WeeklyStartDay;
             
             var firstJan = new DateTime(year, 1, 1);
             var diff = firstJan.DayOfWeek - startDayOfWeek;
             if (diff < 0) diff += 7;
             
             var startOfWeek = firstJan.AddDays(-diff);
             
             int weekNum = 1;
             while (startOfWeek.Year <= year)
             {
                 var endOfWeek = startOfWeek.AddDays(6);
                 
                 if (endOfWeek.Year >= year)
                 {
                     periods.Add(new WorkPeriodDto(
                         $"Semana {weekNum} ({startOfWeek:dd/MM} - {endOfWeek:dd/MM})",
                         startOfWeek,
                         endOfWeek
                     ));
                     weekNum++;
                 }
                 startOfWeek = startOfWeek.AddDays(7);
             }
        }
        else if (config.WorkPeriodMode == WorkPeriodMode.Fortnightly)
        {
             for (int month = 1; month <= 12; month++)
            {
                var monthName = culture.DateTimeFormat.GetMonthName(month);
                monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);

                // Q1: Period (Month-1)*2 + 1
                int p1Num = (month - 1) * 2 + 1;
                var p1Dates = config.GetPeriodDates(year, p1Num);
                periods.Add(new WorkPeriodDto($"{monthName} - Q1 ({p1Dates.Start:dd}-{p1Dates.End:dd})", p1Dates.Start, p1Dates.End));

                // Q2: Period (Month-1)*2 + 2
                int p2Num = p1Num + 1;
                 var p2Dates = config.GetPeriodDates(year, p2Num);
                periods.Add(new WorkPeriodDto($"{monthName} - Q2 ({p2Dates.Start:dd}-{p2Dates.End:dd})", p2Dates.Start, p2Dates.End));
            }
        }
        else if (config.WorkPeriodMode == WorkPeriodMode.Monthly)
        {
            for (int month = 1; month <= 12; month++)
            {
                var monthName = culture.DateTimeFormat.GetMonthName(month);
                monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);
                
                var pDates = config.GetPeriodDates(year, month);
                periods.Add(new WorkPeriodDto($"{monthName} ({pDates.Start:dd/MM} - {pDates.End:dd/MM})", pDates.Start, pDates.End));
            }
        }

        return periods;
    }
}
