﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Connector;
using RightpointLabs.ConferenceRoom.Bot.Criteria;
using RightpointLabs.ConferenceRoom.Bot.Dialogs.Criteria;
using RightpointLabs.ConferenceRoom.Bot.Services;

namespace RightpointLabs.ConferenceRoom.Bot.Dialogs
{
    [Serializable]
    public class BotDialog : LuisDialog<object>
    {
        private readonly Uri _requestUri;
        private List<RoomsService.RoomStatusResult> _roomResults;
        private RoomSearchCriteria _criteria;

        public BotDialog(Uri requestUri) : base(new LuisService(new LuisModelAttribute(Utils.GetAppSetting("LuisAppId"), Utils.GetAppSetting("LuisAPIKey"))))
        {
            _requestUri = requestUri;
        }

        public override Task StartAsync(IDialogContext context)
        {
            return base.StartAsync(context);
        }

        protected override Task MessageReceived(IDialogContext context, IAwaitable<IMessageActivity> item)
        {
            return base.MessageReceived(context, item);
        }

        [LuisIntent("None")]
        public async Task NoneIntent(IDialogContext context, LuisResult result)
        {
            await context.PostAsync(context.CreateMessage($"Sorry, I don't know what you meant.  You said: {result.Query}", InputHints.AcceptingInput));
            context.Done(string.Empty);
        }

        [LuisIntent("findRoom")]
        public async Task FindRoom(IDialogContext context, LuisResult result)
        {
            var criteria = RoomSearchCriteria.ParseCriteria(result);
            await context.Forward(new RoomSearchCriteriaDialog(criteria), DoRoomSearch, context.Activity, new CancellationToken());
        }

        private async Task DoRoomSearch(IDialogContext context, IAwaitable<RoomSearchCriteria> result)
        {
            var criteria = await result;
            if (null == criteria)
            {
                context.Done(string.Empty);
                return;
            }
            await context.Forward(new FindRoomDialog(criteria, _requestUri), Done, context.Activity, new CancellationToken());
        }

        [LuisIntent("bookRoom")]
        public async Task BookRoom(IDialogContext context, LuisResult result)
        {
            var criteria = RoomBookingCriteria.ParseCriteria(result);
            await context.Forward(new RoomBookingCriteriaDialog(criteria), DoBookRoom, context.Activity, new CancellationToken());
        }

        private async Task DoBookRoom(IDialogContext context, IAwaitable<RoomBookingCriteria> result)
        {
            var criteria = await result;
            if (null == criteria)
            {
                context.Done(string.Empty);
                return;
            }
            await context.Forward(new BookRoomDialog(criteria, _requestUri), Done, context.Activity, new CancellationToken());
        }

        [LuisIntent("checkRoom")]
        public async Task CheckRoom(IDialogContext context, LuisResult result)
        {
            var criteria = RoomStatusCriteria.ParseCriteria(result);
            await context.Forward(new RoomStatusCriteriaDialog(criteria), DoRoomCheck, context.Activity, new CancellationToken());
        }

        private async Task DoRoomCheck(IDialogContext context, IAwaitable<RoomStatusCriteria> result)
        {
            var criteria = await result;
            if (null == criteria)
            {
                context.Done(string.Empty);
                return;
            }
            await context.Forward(new CheckRoomDialog(criteria, _requestUri), Done, context.Activity, new CancellationToken());
        }

        [LuisIntent("setBuilding")]
        public async Task SetBuilding(IDialogContext context, LuisResult result)
        {
            var buildingName = result.Entities.Where(i => i.Type == "building").Select(i => i.Entity).FirstOrDefault();
            await context.Forward(new ChooseBuildingDialog(_requestUri, buildingName), SetBuildingCallback, context.Activity, new CancellationToken());
        }

        private async Task SetBuildingCallback(IDialogContext context, IAwaitable<string> result)
        {
            var buildingId = await result;
            if (!string.IsNullOrEmpty(buildingId))
            {
                context.SetBuildingId(buildingId);
                await context.PostAsync(context.CreateMessage($"Building set.", InputHints.AcceptingInput));
            }
            context.Done(string.Empty);
        }

        [LuisIntent("setFloor")]
        public async Task SetFloor(IDialogContext context, LuisResult result)
        {
            var buildingName = result.Entities.Where(i => i.Type == "floor").Select(i => i.Entity).FirstOrDefault();
            await context.Forward(new ChooseFloorDialog(_requestUri, buildingName), SetFloorCallback, context.Activity, new CancellationToken());
        }

        private async Task SetFloorCallback(IDialogContext context, IAwaitable<string> result)
        {
            var floorId = await result;
            if (!string.IsNullOrEmpty(floorId))
            {
                context.SetPreferredFloorId(floorId);
                await context.PostAsync(context.CreateMessage($"Preferred floor set.", InputHints.AcceptingInput));
            }
            context.Done(string.Empty);
        }

        [LuisIntent("clearFloor")]
        public async Task ClearFloor(IDialogContext context, LuisResult result)
        {
            context.SetPreferredFloorId(null);
            await context.PostAsync(context.CreateMessage($"Preferred floor cleared.", InputHints.AcceptingInput));
            context.Done(string.Empty);
        }

        private async Task Done(IDialogContext context, IAwaitable<string> result)
        {
            context.Done(string.Empty);
        }
    }
}

