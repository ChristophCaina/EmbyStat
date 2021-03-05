﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using EmbyStat.Clients.Tvdb;
using EmbyStat.Clients.Tvdb.Models;
using FluentAssertions;
using MediaBrowser.Model.Net;
using Moq;
using RestSharp;
using RestSharp.Serialization.Json;
using Xunit;

namespace Tests.Unit.Clients
{
    public class TvdbHttpClientTests
    {
        private Mock<IRestClient> _restClientMock;
        private IRestRequest _usedRequest;

        private TvdbClient CreateClient<T>(T returnObject, HttpStatusCode statusCode)
        {
            var response = new RestResponse<T> { Data = returnObject, StatusCode = statusCode };

            _restClientMock = new Mock<IRestClient>();
            _restClientMock.Setup(x => x.Execute<T>(It.IsAny<IRestRequest>()))
                .Callback<IRestRequest>((request) =>
                {
                    _usedRequest = request;
                })
                .Returns(response);

            _restClientMock.Setup(x => x.UseSerializer(It.IsAny<JsonDeserializer>)).Returns(_restClientMock.Object);

            return new TvdbClient(_restClientMock.Object);
        }

        [Fact]
        public void Login_Should_Set_JwToken_Object()
        {
            var returnObj = new TvdbToken
            {
                Token = "1234"
            };
            var client = CreateClient(returnObj, HttpStatusCode.OK);

            client.Login("12345");
            _usedRequest.Should().NotBeNull();

            _usedRequest.Parameters.Count.Should().Be(1);
            _usedRequest.Parameters[0].Value.ToString().Should().Be(new { apikey = 12345 }.ToString());
            _usedRequest.Parameters[0].ContentType.Should().Be("application/json");
            _usedRequest.Parameters[0].DataFormat.Should().Be(DataFormat.Json);
            _usedRequest.Parameters[0].Type.Should().Be(ParameterType.RequestBody);
        }

        [Fact]
        public void GetEpisodes_Should_Return_Virtual_Episode_List()
        {
            var returnObject = new TvdbEpisodes()
            {
                Links = new Links
                {
                    Next = null
                },
                Data = new List<Data>(2)
                {
                    new Data
                    {
                        Id = 1,
                        AiredEpisodeNumber = 1,
                        AiredSeason = 1,
                        FirstAired = new DateTime(2019, 10, 20).ToString("O"),
                        EpisodeName = "test1"
                    },new Data
                    {
                        Id = 2,
                        AiredEpisodeNumber = 2,
                        AiredSeason = 2,
                        FirstAired = new DateTime(2019, 10, 21).ToString("O"),
                        EpisodeName = "test2"
                    }
                }
            };
            var client = CreateClient(returnObject, HttpStatusCode.OK);

            var result = client.GetEpisodes("12").ToList();
            result.Count.Should().Be(2);

            result[0].Id.Should().Be(returnObject.Data[0].Id.ToString());
            result[0].EpisodeNumber.Should().Be(returnObject.Data[0].AiredEpisodeNumber);
            // ReSharper disable once PossibleInvalidOperationException
            result[0].FirstAired.Value.ToString("O").Should().Be(returnObject.Data[0].FirstAired);
            result[0].Name.Should().Be(returnObject.Data[0].EpisodeName);
            result[0].SeasonNumber.Should().Be(returnObject.Data[0].AiredSeason);

            result[1].Id.Should().Be(returnObject.Data[1].Id.ToString());
            result[1].EpisodeNumber.Should().Be(returnObject.Data[1].AiredEpisodeNumber);
            // ReSharper disable once PossibleInvalidOperationException
            result[1].FirstAired.Value.ToString("O").Should().Be(returnObject.Data[1].FirstAired);
            result[1].Name.Should().Be(returnObject.Data[1].EpisodeName);
            result[1].SeasonNumber.Should().Be(returnObject.Data[1].AiredSeason);
        }

        [Fact]
        public void GetEpisodes_Should_Return_Virtual_Episode_List_When_FirstAired_Is_Wrong_Format()
        {
            var returnObject = new TvdbEpisodes()
            {
                Links = new Links
                {
                    Next = null
                },
                Data = new List<Data>(2)
                {
                    new Data
                    {
                        Id = 1,
                        AiredEpisodeNumber = 1,
                        AiredSeason = 1,
                        FirstAired = "0000-00-00",
                        EpisodeName = "test1"
                    }
                }
            };
            var client = CreateClient(returnObject, HttpStatusCode.OK);

            var result = client.GetEpisodes("12").ToList();
            result.Count.Should().Be(0);
        }

        [Fact]
        public void GetEpisodes_Should_Return_Virtual_Episode_List_When_FirstAired_Is_Empty()
        {
            var returnObject = new TvdbEpisodes()
            {
                Links = new Links
                {
                    Next = null
                },
                Data = new List<Data>(2)
                {
                    new Data
                    {
                        Id = 1,
                        AiredEpisodeNumber = 1,
                        AiredSeason = 1,
                        FirstAired = "",
                        EpisodeName = "test1"
                    }
                }
            };
            var client = CreateClient(returnObject, HttpStatusCode.OK);

            var result = client.GetEpisodes("12").ToList();
            result.Count.Should().Be(0);
        }

        [Fact]
        public void GetEpisodes_Should_Skip_Episodes_That_Are_Not_Aired_Yet()
        {
            var returnObject = new TvdbEpisodes
            {
                Links = new Links
                {
                    Next = null
                },
                Data = new List<Data>(2)
                {
                    new Data
                    {
                        Id = 1,
                        AiredEpisodeNumber = 1,
                        AiredSeason = 1,
                        FirstAired = new DateTime(2019, 10, 20).ToString("O"),
                        EpisodeName = "test1"
                    },new Data
                    {
                        Id = 2,
                        AiredEpisodeNumber = 2,
                        AiredSeason = 2,
                        FirstAired = DateTime.Now.AddDays(1).ToString("O"),
                        EpisodeName = "test2"
                    }
                }
            };
            var client = CreateClient(returnObject, HttpStatusCode.OK);

            var result = client.GetEpisodes("12").ToList();
            result.Count.Should().Be(1);

            result[0].Id.Should().Be(returnObject.Data[0].Id.ToString());
            result[0].EpisodeNumber.Should().Be(returnObject.Data[0].AiredEpisodeNumber);
            // ReSharper disable once PossibleInvalidOperationException
            result[0].FirstAired.Value.ToString("O").Should().Be(returnObject.Data[0].FirstAired);
            result[0].Name.Should().Be(returnObject.Data[0].EpisodeName);
            result[0].SeasonNumber.Should().Be(returnObject.Data[0].AiredSeason);
        }

        [Fact]
        public void GetEpisodes_Should_Return_Exception_If_Show_Not_Found()
        {
            var client = CreateClient(new TvdbEpisodes(), HttpStatusCode.NotFound);

            Action act = () => client.GetEpisodes("12");

            act.Should().Throw<HttpException>()
                .WithMessage("404 Not Found");
        }
    }
}
