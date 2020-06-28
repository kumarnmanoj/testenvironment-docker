﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Writers;
using TestEnvironment.Docker.DockerApi.Abstractions.Models;
using TestEnvironment.Docker.DockerApi.Abstractions.Services;
using TestEnvironment.Docker.DockerApi.Internal.Services;

namespace TestEnvironment.Docker
{
    public class DockerEnvironment : ITestEnvironment
    {
        private readonly string[] _ignoredFiles;
        private readonly ILogger _logger;
        private readonly IDockerImagesService _dockerImagesService;

        public DockerEnvironment(string name, IDictionary<string, string> variables, IDependency[] dependencies, DockerClient dockerClient, string[] ignoredFiles = null, ILogger logger = null)
        {
            Name = name;
            Variables = variables;
            Dependencies = dependencies;
            _ignoredFiles = ignoredFiles;
            _logger = logger;
            _dockerImagesService = new DockerImagesService(dockerClient);
        }

        public string Name { get; }

        public IDictionary<string, string> Variables { get; }

        public IDependency[] Dependencies { get; }

        public async Task Up(CancellationToken token = default)
        {
            await BuildRequiredImages(token);

            await PullRequiredImages(token);

            await Task.WhenAll(Dependencies.Select(d => d.Run(Variables, token)));
        }

        public Task Down(CancellationToken token = default) =>
            Task.WhenAll(Dependencies.Select(d => d.Stop(token)));

        public Container GetContainer(string name) =>
            Dependencies.FirstOrDefault(d => d is Container c && c.Name.Equals(name.GetContainerName(Name), StringComparison.OrdinalIgnoreCase)) as Container;

        public TContainer GetContainer<TContainer>(string name)
            where TContainer : Container => GetContainer(name) as TContainer;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsync(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var dependency in Dependencies)
                {
                    dependency.Dispose();
                }
            }
        }

        protected virtual async ValueTask DisposeAsync(bool disposing)
        {
            if (disposing)
            {
                var disposeTasks = Dependencies.Select(d => d.DisposeAsync()).ToArray();
                foreach (var dt in disposeTasks)
                {
                    await dt;
                }
            }
        }

        private async Task BuildRequiredImages(CancellationToken token)
        {
            foreach (var container in Dependencies.OfType<ContainerFromDockerfile>())
            {
                var tempFileName = Guid.NewGuid().ToString();
                var contextDirectory = container.Context.Equals(".") ? Directory.GetCurrentDirectory() : container.Context;

                try
                {
                    // In order to pass the context we have to create tar file and use it as an argument.
                    CreateTarArchive();

                    // Now call docker api.
                    var configuration = new ImageFromDockerfileConfiguration(container.ImageName, container.Tag, container.Dockerfile, container.BuildArgs);
                    await _dockerImagesService.BuildImage(configuration, tempFileName, token);
                }
                catch (Exception exc)
                {
                    _logger?.LogError(exc, $"Unable to create the image from dockerfile.");
                    throw;
                }

                // And don't forget to remove created tar.
                try
                {
                    File.Delete(tempFileName);
                }
                catch (Exception exc)
                {
                    _logger?.LogError(exc, $"Unable to delete tar file {tempFileName} with context. Please, cleanup manually.");
                }

                void CreateTarArchive()
                {
                    using (var stream = File.OpenWrite(tempFileName))
                    using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, CompressionType.None))
                    {
                        AddDirectoryFilesToTar(writer, contextDirectory, true);
                    }
                }

                // Adds recuresively files to tar archive.
                void AddDirectoryFilesToTar(IWriter writer, string sourceDirectory, bool recurse)
                {
                    if (_ignoredFiles?.Any(excl => excl.Equals(Path.GetFileName(sourceDirectory))) == true)
                    {
                        return;
                    }

                    // Write each file to the tar.
                    var filenames = Directory.GetFiles(sourceDirectory);
                    foreach (string filename in filenames)
                    {
                        if (Path.GetFileName(filename).Equals(tempFileName))
                        {
                            continue;
                        }

                        if (new FileInfo(filename).Attributes.HasFlag(FileAttributes.Hidden))
                        {
                            continue;
                        }

                        // Make sure that we can read the file
                        try
                        {
                            File.OpenRead(filename);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        try
                        {
                            var contextDirectoryIndex = filename.IndexOf(contextDirectory);
                            var cleanPath = (contextDirectoryIndex < 0)
                                ? filename
                                : filename.Remove(contextDirectoryIndex, contextDirectory.Length);

                            writer.Write(cleanPath, filename);
                        }
                        catch (Exception exc)
                        {
                            _logger?.LogWarning($"Can not add file {filename} to the context: {exc.Message}.");
                        }
                    }

                    if (recurse)
                    {
                        var directories = Directory.GetDirectories(sourceDirectory);
                        foreach (var directory in directories)
                        {
                            AddDirectoryFilesToTar(writer, directory, recurse);
                        }
                    }
                }
            }
        }

        private async Task PullRequiredImages(CancellationToken token)
        {
            foreach (var contianer in Dependencies.OfType<Container>())
            {
                var imageExists = await _dockerImagesService.IsExists(contianer.ImageName, contianer.Tag, token);

                if (!imageExists)
                {
                    _logger.LogInformation($"Pulling the image {contianer.ImageName}:{contianer.Tag}");

                    // Pull the image.
                    try
                    {
                        await _dockerImagesService.PullImage(
                            contianer.ImageName,
                            contianer.Tag,
                            (imageName, tag, message) => _logger.LogDebug($"Pulling image {imageName}:{tag}:\n{message}"),
                            token);
                    }
                    catch (Exception e)
                    {
                        _logger?.LogError(e, $"Unable to pull the image {contianer.ImageName}:{contianer.Tag}");
                        throw;
                    }
                }
            }
        }
    }
}
