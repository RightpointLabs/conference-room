﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

namespace RightpointLabs.ConferenceRoom.Bot.Dialogs
{
    [Serializable]
    public abstract class RoomNinjaDialogBase : IDialog<string>
    {
        public abstract Task StartAsync(IDialogContext context);

        protected async Task BookIt(IDialogContext context, string roomId, DateTime? criteriaStartTime, DateTime? criteriaEndTime)
        {
            await context.PostAsync(context.CreateMessage("Booking not implemented yet.", InputHints.AcceptingInput));
            context.Done(string.Empty);
        }

        protected TimeZoneInfo GetTimezone(RoomSearchCriteria.OfficeOptions office)
        {
            switch (office)
            {
                case RoomSearchCriteria.OfficeOptions.Atlanta:
                case RoomSearchCriteria.OfficeOptions.Boston:
                case RoomSearchCriteria.OfficeOptions.Detroit:
                    return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                case RoomSearchCriteria.OfficeOptions.Chicago:
                case RoomSearchCriteria.OfficeOptions.Dallas:
                    return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                case RoomSearchCriteria.OfficeOptions.Denver:
                    return TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");
                case RoomSearchCriteria.OfficeOptions.Los_Angeles:
                    return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            }
            return null;
        }
    }
}
