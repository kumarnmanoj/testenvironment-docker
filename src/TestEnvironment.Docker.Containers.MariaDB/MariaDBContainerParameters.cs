﻿namespace TestEnvironment.Docker.Containers.MariaDB
{
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
    public record MariaDBContainerParameters(string Name, string RootPassword)
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
        : ContainerParameters(Name, "mariadb")
    {
    }
}
