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
using RightpointLabs.ConferenceRoom.Domain.Models.Entities;
using RightpointLabs.ConferenceRoom.Domain.Repositories;
using RightpointLabs.ConferenceRoom.Domain.Services;
using Task = System.Threading.Tasks.Task;

namespace RightpointLabs.ConferenceRoom.Infrastructure.Services
{
    public class BuildingService : IBuildingService
    {
        private static readonly ILog __log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly IBuildingRepository _buildingRepository;

        public BuildingService(IBuildingRepository buildingRepository)
        {
            _buildingRepository = buildingRepository;
        }

        public void Add(BuildingEntity buildingInfo)
        {
            if (string.IsNullOrWhiteSpace(buildingInfo.Id))
            {
                throw new ArgumentNullException("buildingInfo.Id", "ID cannot be null or whitespace");
            }
            Normalize(buildingInfo);
            _buildingRepository.Save(buildingInfo.Id, buildingInfo);
        }

        public BuildingEntity Get(string buildingId)
        {
            var buildingInfo = _buildingRepository.Get(buildingId) ?? new BuildingEntity();
            if (buildingInfo == null ||
                string.IsNullOrWhiteSpace(buildingInfo.Id))
            {
                throw new NullReferenceException(string.Format("Building {0} not found", buildingId));
            }
            return buildingInfo;
        }

        public void Update(string buildingId, BuildingEntity buildingInfo)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
            {
                throw new ArgumentNullException("buildingId", "ID cannot be null or whitespace");
            }
            var existingBuilding = _buildingRepository.Get(buildingId) ?? new BuildingEntity();
            if (existingBuilding == null)
            {
                throw  new NullReferenceException(string.Format("Building {0} not found", buildingId));
            }

            Normalize(buildingInfo);

            existingBuilding.Name = buildingInfo.Name;
            existingBuilding.StreetAddress1 = buildingInfo.StreetAddress1;
            existingBuilding.StreetAddress2 = buildingInfo.StreetAddress2;
            existingBuilding.City = buildingInfo.City;
            existingBuilding.StateOrProvence = buildingInfo.StateOrProvence;
            existingBuilding.PostalCode = buildingInfo.PostalCode;

            _buildingRepository.Save(existingBuilding.Id, existingBuilding);
        }

        private void Normalize(BuildingEntity buildingInfo)
        {
        }
    }
}
