﻿using System.Collections.Generic;
using RightpointLabs.ConferenceRoom.Domain.Models;
using RightpointLabs.ConferenceRoom.Domain.Models.Entities;

namespace RightpointLabs.ConferenceRoom.Domain.Repositories
{
    public interface IGlobalAdministratorRepository : IRepository
    {
        bool IsGlobalAdmin(string username);
        void EnsureRecordExists();
    }
}