using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IUser : IGenericRepository<User>
    {
        Task<User> GetByUsernameAsync(string userName);
        Task<User> GetRefreshTokenAsync(string email);
    }
}