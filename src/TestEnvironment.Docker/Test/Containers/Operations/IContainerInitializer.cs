﻿using System.Threading;
using System.Threading.Tasks;

namespace TestEnvironment.Docker.Test.Containers.Operations
{
    public interface IContainerInitializer
    {
        Task<bool> Initialize<TContainer>(TContainer container, CancellationToken cancellationToken)
            where TContainer : Container;
    }
}
