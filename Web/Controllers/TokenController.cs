﻿using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using RightpointLabs.ConferenceRoom.Domain.Repositories;
using RightpointLabs.ConferenceRoom.Domain.Services;
using RightpointLabs.ConferenceRoom.Infrastructure.Services;

namespace RightpointLabs.ConferenceRoom.Web.Controllers
{
    [RoutePrefix("api/tokens")]
    public class TokenController : ApiController
    {
        private readonly IOrganizationRepository _organizationRepository;
        private readonly ITokenService _tokenService;
        private readonly IContextService _contextService;

        public TokenController(IOrganizationRepository organizationRepository, ITokenService tokenService, IContextService contextService)
        {
            _organizationRepository = organizationRepository;
            _tokenService = tokenService;
            _contextService = contextService;
        }

        [Route("get")]
        public HttpResponseMessage PostGet()
        {
            var cp = ClaimsPrincipal.Current;
            if (null == cp)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Not authenticated") };
            }

            var username = cp.Identities.FirstOrDefault(_ => _.IsAuthenticated && _.AuthenticationType == "AzureAdAuthCookie")?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("No username available") };
            }

            var domain = username.Split('@').Last();
            var org = _organizationRepository.GetByUserDomain(domain);
            if (null == org)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Domain not part of any organization")};
            }

            var token = _tokenService.CreateUserToken(username, org.Id);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(token, Encoding.UTF8) };
        }

        [Route("get")]
        public HttpResponseMessage GetGet()
        {
            return PostGet();
        }

        [Route("info")]
        public object GetInfo()
        {
            var org = _contextService.CurrentOrganization;
            var device = _contextService.CurrentDevice;
            var userId = _contextService.UserId;

            return new
            {
                organization = org?.Id,
                device = device?.Id,
                building = device?.BuildingId,
                controlledRooms = device?.ControlledRoomIds,
                user = userId,
                beaconNamespace = BuildId(org?.Id, 10),
                beaconUid = BuildId(device?.Id, 6),
            };
        }

        [Route("getLongTerm")]
        public HttpResponseMessage PostGetLongTerm()
        {
            if (!_contextService.IsAuthenticated)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Not authenticated") };
            }

            var username = _contextService.UserId;
            var orgId = _contextService.CurrentOrganization?.Id;
            if (string.IsNullOrEmpty(username))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("No username available") };
            }
            if (string.IsNullOrEmpty(orgId))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("No organization available") };
            }
            if (_contextService.TokenStyle == TokenStyle.LongTerm)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Cannot use a long-term key to create another") };
            }

            var token = _tokenService.CreateLongTermUserToken(username, orgId);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(token, Encoding.UTF8) };
        }

        private string BuildId(string deviceId, int length)
        {
            return string.IsNullOrEmpty(deviceId) 
                ? null 
                : string.Join("", SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(deviceId)).Take(length).Select(i => $"{i:x2}"));
        }
    }
}
