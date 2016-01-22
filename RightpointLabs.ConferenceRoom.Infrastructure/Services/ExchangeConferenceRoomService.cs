﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using log4net;
using Microsoft.Exchange.WebServices.Data;
using Microsoft.Rtc.Collaboration;
using RightpointLabs.ConferenceRoom.Domain;
using RightpointLabs.ConferenceRoom.Domain.Models;
using RightpointLabs.ConferenceRoom.Domain.Repositories;
using RightpointLabs.ConferenceRoom.Domain.Services;
using Task = System.Threading.Tasks.Task;

namespace RightpointLabs.ConferenceRoom.Infrastructure.Services
{
    public class ExchangeConferenceRoomService : IConferenceRoomService
    {
        private static ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly IMeetingRepository _meetingRepository;
        private readonly ISecurityRepository _securityRepository;
        private readonly IBroadcastService _broadcastService;
        private readonly IDateTimeService _dateTimeService;
        private readonly IMeetingCacheService _meetingCacheService;
        private readonly IChangeNotificationService _changeNotificationService;
        private readonly IExchangeServiceManager _exchangeServiceManager;
        private readonly ISimpleTimedCache _simpleTimedCache;
        private readonly IInstantMessagingService _instantMessagingService;
        private readonly ISmsMessagingService _smsMessagingService;
        private readonly ISmsAddressLookupService _smsAddressLookupService;
        private readonly ISignatureService _signatureService;
        private readonly bool _ignoreFree;
        private readonly bool _useChangeNotification;
        private bool _impersonateForAllCalls;

        public ExchangeConferenceRoomService(IMeetingRepository meetingRepository, ISecurityRepository securityRepository, IBroadcastService broadcastService, IDateTimeService dateTimeService, IMeetingCacheService meetingCacheService, IChangeNotificationService changeNotificationService, IExchangeServiceManager exchangeServiceManager, ISimpleTimedCache simpleTimedCache, IInstantMessagingService instantMessagingService, ISmsMessagingService smsMessagingService, ISmsAddressLookupService smsAddressLookupService, ISignatureService signatureService)
        {
            _meetingRepository = meetingRepository;
            _securityRepository = securityRepository;
            _broadcastService = broadcastService;
            _dateTimeService = dateTimeService;
            _meetingCacheService = meetingCacheService;
            _changeNotificationService = changeNotificationService;
            _exchangeServiceManager = exchangeServiceManager;
            _simpleTimedCache = simpleTimedCache;
            _instantMessagingService = instantMessagingService;
            _smsMessagingService = smsMessagingService;
            _smsAddressLookupService = smsAddressLookupService;
            _signatureService = signatureService;
            _ignoreFree = bool.Parse(ConfigurationManager.AppSettings["ignoreFree"] ?? "false");
            _useChangeNotification = bool.Parse(ConfigurationManager.AppSettings["useChangeNotification"] ?? "true");
            _impersonateForAllCalls = bool.Parse(ConfigurationManager.AppSettings["impersonateForAllCalls"] ?? "true");
        }

        /// <summary>
        /// Get all room lists defined on the Exchange server.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<RoomList> GetRoomLists()
        {
            return _simpleTimedCache.GetCachedValue("RoomLists", TimeSpan.FromHours(24),
                () => Task.FromResult(_exchangeServiceManager.Execute("", svc => svc.GetRoomLists().Select(i => new RoomList { Address = i.Address, Name = i.Name }).ToArray()))).Result;
        }

        /// <summary>
        /// Gets all the rooms in the specified room list.
        /// </summary>
        /// <param name="roomListAddress">The room list address returned from <see cref="GetRoomLists"/></param>
        /// <returns></returns>
        public IEnumerable<Room> GetRoomsFromRoomList(string roomListAddress)
        {
            return _simpleTimedCache.GetCachedValue("Rooms_" + roomListAddress, TimeSpan.FromHours(24), 
                () => Task.FromResult(_exchangeServiceManager.Execute("", svc => svc.GetRooms(roomListAddress).Select(i => new Room { Address = i.Address, Name = i.Name }).ToArray()))).Result;
        }

        public object GetInfo(string roomAddress, string securityKey = null)
        {
            var room = _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc => svc.ResolveName(roomAddress).SingleOrDefault());

            if (null == room)
            {
                return null;
            }

            var rights = _securityRepository.GetSecurityRights(roomAddress, securityKey);
            if (rights == SecurityStatus.Granted && _useChangeNotification)
            {
                // make sure we track rooms we're controlling
                _changeNotificationService.TrackRoom(roomAddress);
            }

            return new
            {
                CurrentTime = _dateTimeService.Now,
                DisplayName = room.Mailbox.Name,
                SecurityStatus = rights
            };
        }

        public void RequestAccess(string roomAddress, string securityKey, string clientInfo)
        {
            _securityRepository.RequestAccess(roomAddress, securityKey, clientInfo);
        }

        public IEnumerable<Meeting> GetUpcomingAppointmentsForRoom(string roomAddress)
        {
            var isTracked = _changeNotificationService.IsTrackedForChanges(roomAddress);
            return _meetingCacheService.GetUpcomingAppointmentsForRoom(roomAddress, isTracked, () =>
            {
                return Task.Run(() =>
                {
                    try
                    {
                        return _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc =>
                        {
                            var apt = svc.FindAppointments(WellKnownFolderName.Calendar, new CalendarView(_dateTimeService.Now.Date, _dateTimeService.Now.Date.AddDays(2)) { PropertySet = new PropertySet(AppointmentSchema.Id, AppointmentSchema.LegacyFreeBusyStatus)}).ToList();
                            log.DebugFormat("Got {0} appointments for {1} via {2} with {3}", apt.Count, roomAddress, svc.GetHashCode(), svc.CookieContainer.GetCookieHeader(svc.Url));
                            if (_ignoreFree)
                            {
                                apt = apt.Where(i => i.LegacyFreeBusyStatus != LegacyFreeBusyStatus.Free).ToList();
                            }

                            // now that we have the items, load the data (can't load attendees in the FindAppointments call...)
                            svc.LoadPropertiesForItems(apt, new PropertySet(
                                AppointmentSchema.Id,
                                AppointmentSchema.Subject,
                                AppointmentSchema.Sensitivity,
                                AppointmentSchema.Organizer,
                                AppointmentSchema.Start,
                                AppointmentSchema.End,
                                AppointmentSchema.IsAllDayEvent,
                                AppointmentSchema.RequiredAttendees, 
                                AppointmentSchema.OptionalAttendees));

                            var meetings = _meetingRepository.GetMeetingInfo(apt.Select(i => i.Id.UniqueId).ToArray()).ToDictionary(i => i.Id);
                            return apt.Select(i => BuildMeeting(i, meetings.TryGetValue(i.Id.UniqueId) ?? new MeetingInfo() { Id = i.Id.UniqueId })).ToArray().AsEnumerable();
                        });
                    }
                    catch (ServiceResponseException ex)
                    {
                        if (ex.ErrorCode == ServiceError.ErrorFolderNotFound || ex.ErrorCode == ServiceError.ErrorNonExistentMailbox || ex.ErrorCode == ServiceError.ErrorAccessDenied)
                        {
                            log.DebugFormat("Access denied ({0}) getting appointments for {1}", ex.ErrorCode, roomAddress);
                            throw new AccessDeniedException("Folder/mailbox not found or access denied", ex);
                        }
                        log.DebugFormat("Unexpected error ({0}) getting appointments for {1}", ex.ErrorCode, roomAddress);
                        throw;
                    }
                });
            }).Result;
        }

        public RoomStatusInfo GetStatus(string roomAddress)
        {
            var now = _dateTimeService.Now;
            var allMeetings = GetUpcomingAppointmentsForRoom(roomAddress)
                .OrderBy(i => i.Start).ToList();
            var meetings = allMeetings
                    .Where(i => !i.IsCancelled && !i.IsEndedEarly && i.End > now)
                    .Take(2)
                    .ToList();

            var prev = allMeetings.LastOrDefault(i => i.End < now);
            var current = meetings.FirstOrDefault();
            var isTracked = _changeNotificationService.IsTrackedForChanges(roomAddress);

            var info = new RoomStatusInfo
            {
                IsTrackingChanges = isTracked,
                NearTermMeetings = allMeetings.ToArray(),
                PreviousMeeting = prev,
                CurrentMeeting = current,
                NextMeeting = meetings.Skip(1).FirstOrDefault(),
            };
            if (null == current)
            {
                info.Status = RoomStatus.Free;
            }
            else if (now < current.Start)
            {
                info.Status = current.IsStarted ? RoomStatus.Busy : RoomStatus.Free;
                info.NextChangeSeconds = current.Start.Subtract(now).TotalSeconds;
            }
            else
            {
                info.Status = current.IsStarted ? RoomStatus.Busy : RoomStatus.BusyNotConfirmed;
                info.NextChangeSeconds = current.End.Subtract(now).TotalSeconds;
            }

            return info;
        }

        public void StartMeeting(string roomAddress, string uniqueId, string securityKey)
        {
            SecurityCheck(roomAddress, uniqueId, securityKey);
            log.DebugFormat("Starting {0} for {1}", uniqueId, roomAddress);
            _meetingRepository.StartMeeting(uniqueId);
            BroadcastUpdate(roomAddress);
        }

        public bool StartMeetingFromClient(string roomAddress, string uniqueId, string signature)
        {
            if (!_signatureService.VerifySignature(uniqueId, signature))
            {
                log.ErrorFormat("Invalid signature: {0} for {1}", signature, uniqueId);
                return false;
            }
            log.DebugFormat("Starting {0} for {1}", uniqueId, roomAddress);
            _meetingRepository.StartMeeting(uniqueId);
            BroadcastUpdate(roomAddress);
            return true;
        }

        public void WarnMeeting(string roomAddress, string uniqueId, string securityKey, Func<string, string> buildUrl)
        {
            var meeting = SecurityCheck(roomAddress, uniqueId, securityKey);
            if (meeting.IsNotManaged)
            {
                throw new Exception("Cannot manage this meeting");
            }

            var item = _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc => Appointment.Bind(svc, new ItemId(uniqueId)));
            log.DebugFormat("Warning {0} for {1}, which should start at {2}", uniqueId, roomAddress, item.Start);
            var startUrl = buildUrl(_signatureService.GetSignature(uniqueId));
            SendEmail(item, string.Format("WARNING: your meeting '{0}' in {1} is about to be cancelled.", item.Subject, item.Location), "Use the conference room management device to start the meeting ASAP, or go to " + startUrl + " .");
        }

        public void CancelMeeting(string roomAddress, string uniqueId, string securityKey)
        {
            var meeting = SecurityCheck(roomAddress, uniqueId, securityKey);
            if (meeting.IsNotManaged)
            {
                throw new Exception("Cannot manage this meeting");
            }
            _meetingRepository.CancelMeeting(uniqueId);

            var item = _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc =>
            {
                var appt = Appointment.Bind(svc, new ItemId(uniqueId));
                log.DebugFormat("Cancelling {0} for {1}, which should start at {2}", uniqueId, roomAddress, appt.Start);
                var now = _dateTimeService.Now.TruncateToTheMinute();
                if (now >= appt.Start)
                {
                    appt.End = now;
                }
                else
                {
                    appt.End = appt.Start;
                }
                appt.Update(ConflictResolutionMode.AlwaysOverwrite, SendInvitationsOrCancellationsMode.SendToNone);
                return appt;
            });

            SendEmail(item, string.Format("Your meeting '{0}' in {1} has been cancelled.", item.Subject, item.Location), "If you want to keep the room, use the conference room management device to start a new meeting ASAP.");

            BroadcastUpdate(roomAddress);
        }

        public void EndMeeting(string roomAddress, string uniqueId, string securityKey)
        {
            var meeting = SecurityCheck(roomAddress, uniqueId, securityKey);
            if (meeting.IsNotManaged)
            {
                throw new Exception("Cannot manage this meeting");
            }
            _meetingRepository.EndMeeting(uniqueId);

            var now = _dateTimeService.Now.TruncateToTheMinute();

            _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc =>
            {
                var item = Appointment.Bind(svc, new ItemId(uniqueId));
                log.DebugFormat("Ending {0} for {1}, which should start at {2}", uniqueId, roomAddress, item.Start);
                if (now >= item.Start)
                {
                    item.End = now;
                }
                else
                {
                    item.End = item.Start;
                }
                item.Update(ConflictResolutionMode.AlwaysOverwrite, SendInvitationsOrCancellationsMode.SendToNone);
            });

            BroadcastUpdate(roomAddress);
        }

        public void MessageMeeting(string roomAddress, string uniqueId, string securityKey)
        {
            var meeting = SecurityCheck(roomAddress, uniqueId, securityKey);
            if (meeting.IsNotManaged)
            {
                throw new Exception("Cannot manage this meeting");
            }

            var item = _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc => Appointment.Bind(svc, new ItemId(uniqueId), new PropertySet(AppointmentSchema.RequiredAttendees, AppointmentSchema.OptionalAttendees, AppointmentSchema.Location)));
            var addresses = item.RequiredAttendees.Concat(item.OptionalAttendees).Select(i => i.Address).Where(i => i != null && i.ToLower().EndsWith("@rightpoint.com")).ToArray();

            var smsAddresses = _smsAddressLookupService.LookupAddresses(addresses);

            if (smsAddresses.Any())
            {
                _smsMessagingService.Send(smsAddresses, string.Format("Your meeting in {0} is over - please finish up ASAP - others are waiting outside.", item.Location));
            }
            if (addresses.Any())
            {
                _instantMessagingService.SendMessage(addresses, string.Format("Meeting in {0} is over", item.Location), string.Format("Your meeting in {0} is over - people for the next meeting are patiently waiting at the door. Please wrap up ASAP.", item.Location), InstantMessagePriority.Urgent);
            }
        }

        public void StartNewMeeting(string roomAddress, string securityKey, string title, int minutes)
        {
            if (_securityRepository.GetSecurityRights(roomAddress, securityKey) != SecurityStatus.Granted)
            {
                throw new UnauthorizedAccessException();
            }
            var status = GetStatus(roomAddress);
            if (status.Status != RoomStatus.Free)
            {
                throw new Exception("Room is not free");
            }

            var item = _exchangeServiceManager.Execute(_impersonateForAllCalls ? roomAddress : "", svc =>
            {
                var calId = new FolderId(WellKnownFolderName.Calendar, new Mailbox(roomAddress));

                var now = _dateTimeService.Now.TruncateToTheMinute();
                minutes = Math.Max(0, Math.Min(minutes, Math.Min(120, status.NextMeeting.ChainIfNotNull(m => (int?)m.Start.Subtract(now).TotalMinutes) ?? 120)));

                var appt = new Appointment(svc);
                appt.Start = now;
                appt.End = now.AddMinutes(minutes);
                appt.Subject = title;
                appt.Body = "Scheduled via conference room management system";
                appt.Save(calId, SendInvitationsMode.SendToNone);
                log.DebugFormat("Created {0} for {1}", appt.Id.UniqueId, roomAddress);
                return appt;
            });

            _meetingRepository.StartMeeting(item.Id.UniqueId);
            BroadcastUpdate(roomAddress);
        }

        private void BroadcastUpdate(string roomAddress)
        {
            _meetingCacheService.ClearUpcomingAppointmentsForRoom(roomAddress);
            _broadcastService.BroadcastUpdate(roomAddress);
        }

        private void SendEmail(Appointment item, string subject, string body)
        {
            _exchangeServiceManager.Execute("", svc =>
            {
                var msg = new EmailMessage(svc);
                msg.Subject = subject;
                msg.Body = body;
                log.DebugFormat("Address: {0}, MailboxType: {1}", item.Organizer.Address, item.Organizer.MailboxType);
                if (item.Organizer.MailboxType == MailboxType.Mailbox)
                {
                    msg.ToRecipients.Add(item.Organizer);
                }
                foreach (var x in item.RequiredAttendees.Concat(item.OptionalAttendees))
                {
                    log.DebugFormat("Address: {0}, MailboxType: {1}, RoutingType: {2}", x.Address, x.MailboxType, x.RoutingType);
                    if (x.RoutingType == "SMTP" && x.Address.EndsWith("@rightpoint.com"))
                    {
                        log.DebugFormat("Also sending to {0} @ {1}", x.Name, x.Address);
                        msg.CcRecipients.Add(x.Name, x.Address);
                    }
                }
                msg.Send();
            });
        }

        private Meeting SecurityCheck(string roomAddress, string uniqueId, string securityKey)
        {
            if (_securityRepository.GetSecurityRights(roomAddress, securityKey) != SecurityStatus.Granted)
            {
                throw new UnauthorizedAccessException();
            }
            var meeting = GetUpcomingAppointmentsForRoom(roomAddress).FirstOrDefault(i => i.UniqueId == uniqueId);
            if (null == meeting)
            {
                throw new Exception();
            }
            return meeting;
        }

        private static Meeting BuildMeeting(Appointment i, MeetingInfo meetingInfo)
        {
            var externalAttendees =
                i.RequiredAttendees.Concat(i.OptionalAttendees)
                    .Count(ii => null == ii.Address || !ii.Address.ToLower().EndsWith("@rightpoint.com"));

            return new Meeting
            {
                UniqueId = i.Id.UniqueId,
                Subject = i.Sensitivity != Sensitivity.Normal ? i.Sensitivity.ToString() :
                    i.Subject != null && i.Subject.Trim() == i.Organizer.Name.Trim() ? null : i.Subject,
                Start = i.Start,
                End = i.End,
                Organizer = i.Organizer.Name,
                RequiredAttendees = i.RequiredAttendees.Count,
                OptionalAttendees = i.OptionalAttendees.Count,
                ExternalAttendees = externalAttendees,
                IsStarted = meetingInfo.IsStarted,
                IsEndedEarly = meetingInfo.IsEndedEarly,
                IsCancelled = meetingInfo.IsCancelled,
                IsNotManaged = i.IsAllDayEvent || Math.Abs(i.End.Subtract(i.Start).TotalHours) > 6, // all day events and events longer than 6 hours won't be auto-cancelled
            };
        }

        public static Func<ExchangeService> GetExchangeServiceBuilder(string username, string password, string serviceUrl)
        {
            // if we don't get a service URL in our configuration, run auto-discovery the first time we need it
            var svcUrl = new Lazy<string>(() =>
            {
                if (!string.IsNullOrEmpty(serviceUrl))
                {
                    return serviceUrl;
                }
                var log = log4net.LogManager.GetLogger(MethodInfo.GetCurrentMethod().DeclaringType);
                log.DebugFormat("serviceUrl wasn't configured in appSettings, running auto-discovery");
                var svc = new ExchangeService(ExchangeVersion.Exchange2010_SP1);
                svc.Credentials = new WebCredentials(username, password);
                svc.PreAuthenticate = true;
                svc.AutodiscoverUrl(username, url => new Uri(url).Scheme == "https");
                log.DebugFormat("Auto-discovery complete - found URL: {0}", svc.Url);
                return svc.Url.ToString();
            });

            return () =>
                new ExchangeService(ExchangeVersion.Exchange2010_SP1)
                {
                    Credentials = new WebCredentials(username, password),
                    Url = new Uri(svcUrl.Value),
                    PreAuthenticate = true,
                };

        }
    }
}
