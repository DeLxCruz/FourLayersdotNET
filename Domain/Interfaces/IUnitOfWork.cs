using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using iText.Commons.Actions.Processors;

namespace Domain.Interfaces
{
    public interface IUnitOfWork
    {
        IRol Roles { get; }
        IUser Users { get; }
        Task<int> SaveAsync();
    }
}