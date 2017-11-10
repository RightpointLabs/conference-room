﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Bot.Builder.Luis.Models;

namespace RightpointLabs.ConferenceRoom.Bot.Criteria
{
    [Serializable]
    public class RoomBaseCriteria : BaseCriteria
    {
        public DateTimeOffset? StartTime;
        public DateTimeOffset? EndTime;

        public void LoadTimeCriteria(LuisResult result, TimeZoneInfo timezone)
        {
            var timeRange = ParseTimeRange(result, timezone);
            var time = ParseTime(result, timezone);
            var duration = ParseDuration(result);

            var start = timeRange.HasValue
                ? timeRange.Value.start
                : time.Length >= 1
                    ? time[0]
                    : time.Length == 1 && duration.HasValue
                        ? time[0]
                        : (DateTimeOffset?)null;
            if (start.HasValue && start >= DateTime.Now.AddSeconds(-10) && start <= DateTime.Now.AddSeconds(10))
            {
                // user said "now".. let's adjust a bit
                start = GetAssumedStartTime(start.Value);
            }
            while (start.HasValue && start < DateTime.Now.AddMinutes(-15))
            {
                start = start.Value.AddDays(1);
            }

            var end = timeRange.HasValue
                ? timeRange.Value.end
                : time.Length >= 2
                    ? time[1]
                    : duration.HasValue && start.HasValue
                        ? start.Value.Add(duration.Value)
                        : (DateTimeOffset?)null;
            while (end.HasValue && end < DateTime.Now.AddMinutes(-15))
            {
                end = end.Value.AddDays(1);
            }

            this.StartTime = start;
            this.EndTime = end;
        }

        public void LoadEndTimeCriteria(LuisResult result, TimeZoneInfo timezone)
        {
            var time = ParseTime(result, timezone);
            var duration = ParseDuration(result);

            var end = time.Length >= 1
                ? time[0]
                : duration.HasValue && this.StartTime.HasValue
                    ? this.StartTime.Value.Add(duration.Value)
                    : (DateTimeOffset?)null;
            while (end.HasValue && end < DateTime.Now.AddMinutes(-15))
            {
                end = end.Value.AddDays(1);
            }

            this.EndTime = end;
        }
        private static TimeSpan? ParseDuration(LuisResult result)
        {
            var duration = result.Entities
                .Where(i => i.Type == "builtin.datetimeV2.duration")
                .SelectMany(i => (List<object>) i.Resolution["values"])
                .Select(i => ParseDuration((IDictionary<string, object>) i))
                .FirstOrDefault(i => i.HasValue);
            return duration;
        }

        private static DateTimeOffset[] ParseTime(LuisResult result, TimeZoneInfo timezone)
        {
            var time = result.Entities
                .Where(i => i.Type == "builtin.datetimeV2.time" || i.Type == "builtin.datetimeV2.datetime")
                .Select(x =>
                {
                    var values = ((List<object>) x.Resolution["values"])
                        .Select(i => ParseTime((IDictionary<string, object>) i, timezone))
                        .Where(i => i.HasValue)
                        .ToArray();
                    if (values.Length > 1 && values.Count(i => i > DateTime.Now) != values.Length)
                    {
                        values = values.Where(i => i > DateTimeOffset.Now).ToArray();
                    }
                    return values.FirstOrDefault();
                })
                .Where(i => i.HasValue)
                .Select(i => i.Value)
                .ToArray();
            return time;
        }

        private static (DateTimeOffset start, DateTimeOffset end)? ParseTimeRange(LuisResult result, TimeZoneInfo timezone)
        {
            var timeRange = result.Entities
                .Where(i => i.Type == "builtin.datetimeV2.timerange")
                .SelectMany(i => (List<object>)i.Resolution["values"])
                .Select(i => ParseTimeRange((IDictionary<string, object>)i, timezone))
                .FirstOrDefault(i => i.HasValue);
            return timeRange;
        }
    }
}