﻿using Box.V2;
using Box.V2.Managers;
using Box.V2.Models;
using BotDeScans.App.Services;
using BotDeScans.App.Services.Factories;
using BotDeScans.App.Wrappers;
using FakeItEasy;
using FluentAssertions;
using FluentResults;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace BotDeScans.UnitTests.Specs.Services
{
    public class BoxServiceTests : UnitTest<BoxService>
    {
        private const string rootFolderId = "0";
        private readonly IBoxClient boxClient;
        private readonly StreamWrapper streamWrapper;

        public BoxServiceTests()
        {
            var storageFactory = A.Fake<ExternalServicesFactory>();
            boxClient = A.Fake<IBoxClient>();
            streamWrapper = A.Fake<StreamWrapper>();
            instance = new(storageFactory, streamWrapper);

            A.CallTo(() => storageFactory
                .CreateBoxClient())
                .Returns(Result.Ok(boxClient));
        }

        public class GetOrCreateFolderAsync : BoxServiceTests
        {
            const string folderType = "folder"; 
            const int maxItemsQuery = 1000;
            private readonly IBoxFoldersManager boxFoldersManager;
            private readonly BoxCollection<BoxItem> boxCollection;

            public GetOrCreateFolderAsync()
            {
                boxFoldersManager = A.Fake<IBoxFoldersManager>();
                boxCollection = new BoxCollection<BoxItem>
                {
                    Entries = new List<BoxItem>(new[] 
                    {
                        A.Fake<BoxFolder>(),
                        A.Fake<BoxFolder>(),
                        A.Fake<BoxFolder>()
                    }
                )};

                A.CallTo(() => boxCollection.Entries[0].Name).Returns(dataGenerator.Random.Word());
                A.CallTo(() => boxCollection.Entries[0].Type).Returns(folderType);
                A.CallTo(() => boxCollection.Entries[1].Name).Returns(dataGenerator.Random.Word());
                A.CallTo(() => boxCollection.Entries[1].Type).Returns(folderType);

                A.CallTo(() => boxClient
                    .FoldersManager)
                    .Returns(boxFoldersManager);

                A.CallTo(() => boxFoldersManager
                    .GetFolderItemsAsync(rootFolderId, maxItemsQuery, default, default, default, default, default))
                    .Returns(boxCollection);
            }

            [Fact]
            public async Task ShouldGetFolderWhenItExists()
            {
                var boxItem = await instance.GetOrCreateFolderAsync(boxCollection.Entries[1].Name);
                boxItem.Should().Be(boxCollection.Entries[1]);
            }

            [Fact]
            public async Task ShouldGetFolderWhenItExists_WithParentFolder()
            {
                var folderId = dataGenerator.Random.Word();

                A.CallTo(() => boxFoldersManager
                    .GetFolderItemsAsync(rootFolderId, maxItemsQuery, default, default, default, default, default))
                    .Throws<Exception>();

                A.CallTo(() => boxFoldersManager
                    .GetFolderItemsAsync(folderId, maxItemsQuery, default, default, default, default, default))
                    .Returns(boxCollection);

                var boxItem = await instance.GetOrCreateFolderAsync(boxCollection.Entries[1].Name, folderId);
                boxItem.Should().Be(boxCollection.Entries[1]);
            }

            [Fact]
            public async Task ShouldCreateFolderWhenItDoesNotExists()
            {
                var name = dataGenerator.Random.Word();
                var newBoxItem = A.Fake<BoxFolder>();
                A.CallTo(() => newBoxItem.Name).Returns(name);
                A.CallTo(() => boxFoldersManager
                    .CreateAsync(A<BoxFolderRequest>.That.Matches(x =>
                        x.Name == name &&
                        x.Parent.Id == rootFolderId), null))
                    .Returns(newBoxItem);

                var boxItem = await instance.GetOrCreateFolderAsync(name);
                boxItem.Should().Be(newBoxItem);
            }

            [Fact]
            public async Task ShouldCreateFolderWhenItDoesNotExists_WithParentFolder()
            {
                var folderId = dataGenerator.Random.Word();
                var newBoxItem = A.Fake<BoxFolder>();
                A.CallTo(() => newBoxItem.Name).Returns(dataGenerator.Random.Word());
                A.CallTo(() => boxFoldersManager
                    .CreateAsync(
                        A<BoxFolderRequest>.That.Matches(x =>
                            x.Name == newBoxItem.Name &&
                            x.Parent.Id == folderId), 
                        null))
                    .Returns(newBoxItem);

                var boxItem = await instance.GetOrCreateFolderAsync(newBoxItem.Name, folderId);
                boxItem.Should().Be(newBoxItem);
            }
        }

        public class CreateFileAsync : BoxServiceTests, IDisposable
        {
            private readonly string filePath;
            private readonly string downloadUrl;
            private readonly Stream stream;
            private readonly IBoxFilesManager boxFilesManager;
            private readonly BoxFile boxFile;

            public CreateFileAsync()
            {
                downloadUrl = dataGenerator.Random.Word();
                filePath = Path.Combine("C:", "some-path", "some-file.jpg");
                stream = A.Fake<Stream>();
                boxFilesManager = A.Fake<IBoxFilesManager>();
                boxFile = A.Fake<BoxFile>();
                var boxFileWithSharedLink = A.Fake<BoxFile>();
                var boxSharedLink = A.Fake<BoxSharedLink>();

                A.CallTo(() => boxClient
                    .FilesManager)
                    .Returns(boxFilesManager);

                A.CallTo(() => boxFile
                    .Id)
                    .Returns(dataGenerator.Random.Word());

                A.CallTo(() => streamWrapper
                    .CreateFileStream(filePath, FileMode.Open))
                    .Returns(stream);

                A.CallTo(() => boxFilesManager
                    .UploadAsync(
                        A<BoxFileRequest>.That.Matches(x =>
                            x.Name == "some-file.jpg" &&
                            x.Parent.Id == rootFolderId), 
                        stream, 
                        default, default, default, true, default))
                    .Returns(boxFile);

                A.CallTo(() => boxFilesManager
                    .CreateSharedLinkAsync(
                        boxFile.Id, 
                        A<BoxSharedLinkRequest>.That.Matches(x =>
                            x.Access == BoxSharedLinkAccessType.open &&
                            x.Permissions.Download == true &&
                            x.UnsharedAt == null), 
                        null))
                    .Returns(boxFileWithSharedLink);

                A.CallTo(() => boxFileWithSharedLink
                    .SharedLink)
                    .Returns(boxSharedLink);

                A.CallTo(() => boxSharedLink
                    .DownloadUrl)
                    .Returns(downloadUrl);
            }

            [Fact]
            public async Task ShouldCreateFileSuccessfuly()
            {
               var result = await instance.CreateFileAsync(filePath);
                result.Should().Be(downloadUrl);
            }

            [Fact]
            public async Task ShouldCreateFileSuccessfuly_WithParentFolder()
            {
                var folderId = dataGenerator.Random.Word();

                A.CallTo(() => boxFilesManager
                    .UploadAsync(
                        A<BoxFileRequest>.That.Matches(x =>
                            x.Name == "some-file.jpg" &&
                            x.Parent.Id == rootFolderId),
                        stream,
                        default, default, default, true, default))
                    .Throws<Exception>();

                A.CallTo(() => boxFilesManager
                    .UploadAsync(
                        A<BoxFileRequest>.That.Matches(x =>
                            x.Name == "some-file.jpg" &&
                            x.Parent.Id == folderId),
                        stream,
                        default, default, default, true, default))
                    .Returns(boxFile);

                var result = await instance.CreateFileAsync(filePath, folderId);
                result.Should().Be(downloadUrl);
            }

            public override void Dispose()
            {
                stream?.Dispose();
                base.Dispose();

                GC.SuppressFinalize(this);
            }
        }
    }
}
