﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestEnvironment.Docker.Containers.Ftp
{
#pragma warning disable SA1313 // Parameter names should begin with lower-case letter
    public record FtpContainerParameters(string Name, string FtpUserName, string FtpPassword)
        : ContainerParameters(Name, "stilliard/pure-ftpd")
#pragma warning restore SA1313 // Parameter names should begin with lower-case letter
    {
    }
}