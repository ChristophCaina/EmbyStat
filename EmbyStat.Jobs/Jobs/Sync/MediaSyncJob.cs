using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmbyStat.Clients.Emby.Http;
using EmbyStat.Clients.Emby.Http.Model;
using EmbyStat.Clients.Tvdb;
using EmbyStat.Common;
using EmbyStat.Common.Converters;
using EmbyStat.Common.Extensions;
using EmbyStat.Common.Hubs.Job;
using EmbyStat.Common.Models.Entities;
using EmbyStat.Common.Models.Show;
using EmbyStat.Jobs.Jobs.Interfaces;
using EmbyStat.Repositories;
using EmbyStat.Repositories.Interfaces;
using EmbyStat.Services.Interfaces;
using Hangfire;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Querying;

namespace EmbyStat.Jobs.Jobs.Sync
{
    [DisableConcurrentExecution(60 * 60)]
    public class MediaSyncJob : BaseJob, IMediaSyncJob
    {
        private readonly IEmbyClient _embyClient;
        private readonly IMovieRepository _movieRepository;
        private readonly IShowRepository _showRepository;
        private readonly ILibraryRepository _libraryRepository;
        private readonly ITvdbClient _tvdbClient;
        private readonly IStatisticsRepository _statisticsRepository;
        private readonly IMovieService _movieService;
        private readonly IShowService _showService;

        public MediaSyncJob(IJobHubHelper hubHelper, IJobRepository jobRepository, ISettingsService settingsService,
            IEmbyClient embyClient, IMovieRepository movieRepository, IShowRepository showRepository,
            ILibraryRepository libraryRepository, ITvdbClient tvdbClient, IStatisticsRepository statisticsRepository,
            IMovieService movieService, IShowService showService) : base(hubHelper, jobRepository, settingsService)
        {
            _embyClient = embyClient;
            _movieRepository = movieRepository;
            _showRepository = showRepository;
            _libraryRepository = libraryRepository;
            _tvdbClient = tvdbClient;
            _statisticsRepository = statisticsRepository;
            _movieService = movieService;
            _showService = showService;
            Title = jobRepository.GetById(Id).Title;
        }

        public sealed override Guid Id => Constants.JobIds.MediaSyncId;
        public override string JobPrefix => Constants.LogPrefix.MediaSyncJob;
        public override string Title { get; }

        public override async Task RunJobAsync()
        {
            var cancellationToken = new CancellationToken(false);

            if (!Settings.WizardFinished)
            {
                await LogWarning("Media sync task not running because wizard is not yet finished!");
                return;
            }

            if (!await IsEmbyAliveAsync())
            {
                await LogWarning($"Halting task because we can't contact the Emby server on {Settings.Emby.FullEmbyServerAddress}, please check the connection and try again.");
                //return;
            }

            await LogInformation("First delete all existing media and root media libraries from database so we have a clean start.");
            await LogProgress(3);

            var libraries = await GetLibrariesAsync();
            _libraryRepository.AddOrUpdateRange(libraries);
            await LogInformation($"Found {libraries.Count} root items, getting ready for processing");
            await LogProgress(15);

            await ProcessMoviesAsync(libraries, cancellationToken);
            await LogProgress(35);

            //var oldShows = await ProcessShowsAsync(libraries, cancellationToken);
            await LogProgress(55);

            //await SyncMissingEpisodesAsync(oldShows, cancellationToken);
            await LogProgress(85);

            await CalculateStatistics();
            await LogProgress(100);

            _embyClient.Dispose();
        }

        #region Movies
        private async Task ProcessMoviesAsync(IReadOnlyList<Library> libraries, CancellationToken cancellationToken)
        {
            await LogInformation("Lets start processing movies");
            _movieRepository.RemoveMovies();

            var neededLibraries = libraries.Where(x => Settings.MovieLibraryTypes.Any(y => y == x.Type)).ToList();
            for (var i = 0; i < neededLibraries.Count; i++)
            {
                var totalCount = await GetTotalLibraryMovieCount(neededLibraries[i].Id);
                await LogInformation($"Found {totalCount} movies for {neededLibraries[i].Name} library");
                var processed = 0;
                var j = 0;
                var limit = 100;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var movies = await PerformMovieSyncAsync(neededLibraries[i].Id, j*limit, limit);
                    _movieRepository.UpsertRange(movies);

                    processed += 100;
                    j++;
                    await LogInformation($"Processed { processed } / { totalCount } movies");
;                } while (processed < totalCount);

                await LogProgress(Math.Round(15 + 20 * (i + 1) / (double)neededLibraries.Count, 1));
            }
        }

        private async Task<int> GetTotalLibraryMovieCount(string parentId)
        {
            var countQuery = new ItemQuery
            {
                ParentId = parentId,
                Recursive = true,
                UserId = Settings.Emby.UserId,
                IncludeItemTypes = new[] {nameof(Movie), nameof(Boxset)},
                StartIndex = 0,
                Limit = 1,
                EnableTotalRecordCount = true
            };

            var totalCountResult = await _embyClient.GetItemsAsync(countQuery);
            return totalCountResult.TotalRecordCount;
        }

        private async Task<List<Movie>> PerformMovieSyncAsync(string parentId, int startIndex, int limit)
        {
            var query = new ItemQuery
            {
                EnableImageTypes = new[] { ImageType.Banner, ImageType.Primary, ImageType.Thumb, ImageType.Logo },
                ParentId = parentId,
                Recursive = true,
                LocationTypes = new[] { LocationType.FileSystem },
                UserId = Settings.Emby.UserId,
                IncludeItemTypes = new[] { nameof(Movie), nameof(Boxset) },
                StartIndex = startIndex,
                Limit = limit,
                Fields = new[]
                    {
                        ItemFields.Genres, ItemFields.DateCreated, ItemFields.MediaSources, ItemFields.ExternalUrls,
                        ItemFields.OriginalTitle, ItemFields.Studios, ItemFields.MediaStreams, ItemFields.Path,
                        ItemFields.Overview, ItemFields.ProviderIds, ItemFields.SortName, ItemFields.ParentId,
                        ItemFields.People, ItemFields.PremiereDate, ItemFields.CommunityRating, ItemFields.OfficialRating,
                        ItemFields.ProductionYear, ItemFields.RunTimeTicks
                    }
            };

            try
            {
                var embyMovies = await _embyClient.GetItemsAsync(query);
                var movies = embyMovies.Items
                    .Where(x => x.Type == Constants.Type.Movie)
                    .Select(x => MovieConverter.ConvertToMovie(x, parentId))
                    .ToList();

                if (embyMovies.Items.Any(x => x.Type == Constants.Type.Boxset))
                {
                    foreach (var parent in embyMovies.Items.Where(x => x.Type == Constants.Type.Boxset))
                    {
                        movies.AddRange(await PerformMovieSyncAsync(parent.Id, 0, 1000));
                    }
                }

                embyMovies.Items = new BaseItemDto[0];
                return movies;
            }
            catch (Exception e)
            {
                await LogError($"Movie error: {e.Message}");
                throw;
            }
        }

        #endregion

        #region Shows
        private async Task<IReadOnlyList<Show>> ProcessShowsAsync(IEnumerable<Library> libraries, CancellationToken cancellationToken)
        {
            await LogInformation("Lets start processing shows");
            var oldShows = _showRepository.GetAllShows(Array.Empty<string>(), false, true).ToList();
            _showRepository.RemoveShows();

            var neededLibraries = libraries.Where(x => Settings.ShowLibraryTypes.Any(y => y == x.Type)).ToList();
            for (var i = 0; i < neededLibraries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await PerformShowSyncAsync(oldShows, neededLibraries[i]);
                await LogProgress(Math.Round(35 + 20 * (i + 1) / (double)neededLibraries.Count, 1));
            }

            return oldShows;
        }

        private async Task PerformShowSyncAsync(IReadOnlyList<Show> oldShows, Library library)
        {
            var showList = await GetShowForLibraryAsync(library.Id);
            await LogInformation($"Found {showList.Items.Length} shows for {library.Name} library");

            foreach (var showDto in showList.Items)
            {
                //var totalChildrenCount = await GetShowChildrenCount(showDto.Id);
                var query = new ItemQuery
                {
                    EnableImageTypes = new[] { ImageType.Banner, ImageType.Primary, ImageType.Thumb, ImageType.Logo },
                    ParentId = showDto.Id,
                    LocationTypes = new[] { LocationType.FileSystem },
                    Recursive = true,
                    UserId = Settings.Emby.UserId,
                    IncludeItemTypes = new[] { nameof(Season), nameof(Episode) },
                    Fields = new[]
                    {

                        ItemFields.OriginalTitle,ItemFields.Genres, ItemFields.DateCreated, ItemFields.ExternalUrls,
                        ItemFields.Studios, ItemFields.Path, ItemFields.Overview, ItemFields.ProviderIds,
                        ItemFields.SortName, ItemFields.ParentId, ItemFields.People, ItemFields.MediaSources,
                        ItemFields.MediaStreams, ItemFields.PremiereDate, ItemFields.CommunityRating,
                        ItemFields.OfficialRating, ItemFields.ProductionYear, ItemFields.Status, ItemFields.RunTimeTicks
                    }
                };

                var showChildren = await _embyClient.GetItemsAsync(query);

                var show = ShowConverter.ConvertToShow(showDto, library.Id);
                var seasons = showChildren.Items
                    .Where(x => x.ParentId == show.Id.ToString())
                    .Where(x => x.Type == nameof(Season))
                    .Select(ShowConverter.ConvertToSeason)
                    .ToList();

                var episodes = showChildren.Items
                    .Where(x => seasons.Any(y => y.Id.ToString() == x.ParentId))
                    .Where(x => x.Type == nameof(Episode))
                    .Select(x => ShowConverter.ConvertToEpisode(x, Convert.ToInt32(show.Id)))
                    .ToList();

                show.CumulativeRunTimeTicks = episodes.Sum(x => x.RunTimeTicks ?? 0);
                show.Seasons = seasons;
                show.Episodes = episodes;

                var oldShow = oldShows.SingleOrDefault(x => x.Id == show.Id);
                if (oldShow != null)
                {
                    show.TvdbFailed = oldShow.TvdbFailed;
                    show.TvdbSynced = oldShow.TvdbSynced;
                }

                _showRepository.InsertSeasonsBulk(seasons);
                _showRepository.InsertEpisodesBulk(episodes);
                _showRepository.InsertShow(show);
            }
        }

        private async Task<int> GetShowChildrenCount(string parentId)
        {
            var countQuery = new ItemQuery
            {
                ParentId = parentId,
                Recursive = true,
                UserId = Settings.Emby.UserId,
                IncludeItemTypes = new[] { nameof(Season), nameof(Episode) },
                StartIndex = 0,
                Limit = 1,
                EnableTotalRecordCount = true
            };

            var totalCountResult = await _embyClient.GetItemsAsync(countQuery);
            return totalCountResult.TotalRecordCount;
        }

        private Task<QueryResult<BaseItemDto>> GetShowForLibraryAsync(string libraryId)
        {
            var query = new ItemQuery
            {
                SortBy = new[] { "SortName" },
                SortOrder = SortOrder.Ascending,
                EnableImageTypes = new[] { ImageType.Banner, ImageType.Primary, ImageType.Thumb, ImageType.Logo },
                ParentId = libraryId,
                LocationTypes = new[] { LocationType.FileSystem },
                Recursive = true,
                UserId = Settings.Emby.UserId,
                IncludeItemTypes = new[] { "Series" },
                Fields = new[] {
                    ItemFields.OriginalTitle, ItemFields.Genres, ItemFields.DateCreated, ItemFields.ExternalUrls,
                    ItemFields.Studios, ItemFields.Path, ItemFields.ProviderIds,
                    ItemFields.SortName, ItemFields.ParentId, ItemFields.People, ItemFields.PremiereDate,
                    ItemFields.CommunityRating, ItemFields.OfficialRating, ItemFields.ProductionYear,
                    ItemFields.Status, ItemFields.RunTimeTicks
                }
            };

            return _embyClient.GetItemsAsync(query);

        }

        private async Task SyncMissingEpisodesAsync(IReadOnlyList<Show> oldShows, CancellationToken cancellationToken)
        {
            await LogInformation("Started checking missing episodes");
            await _tvdbClient.Login(Settings.Tvdb.ApiKey, cancellationToken);

            var shows = _showRepository.GetAllShowsWithTvdbId().ToList();
            var showsWithMissingEpisodes = shows.GetNeverSyncedShows().ToList();
            showsWithMissingEpisodes.AddRange(shows.GetShowsWithChangedEpisodes(oldShows));

            if (Settings.Tvdb.LastUpdate.HasValue)
            {
                var showsThatNeedAnUpdate = await _tvdbClient.GetShowsToUpdate(shows.Select(x => x.TVDB), Settings.Tvdb.LastUpdate.Value, cancellationToken);
                showsWithMissingEpisodes.AddRange(shows.Where(x => showsThatNeedAnUpdate.Any(y => y == x.TVDB)));
            }

            showsWithMissingEpisodes = showsWithMissingEpisodes.DistinctBy(x => x.TVDB).ToList();

            await GetMissingEpisodesFromTvdbAsync(showsWithMissingEpisodes, cancellationToken);

            Settings.Tvdb.LastUpdate = DateTime.Now;
            await SettingsService.SaveUserSettingsAsync(Settings);
        }

        private async Task GetMissingEpisodesFromTvdbAsync(IReadOnlyList<Show> shows, CancellationToken cancellationToken)
        {
            await LogInformation($"We need to check {shows.Count} shows for missing episodes");
            for (var i = 0; i < shows.Count; i++)
            {
                try
                {
                    await ProgressMissingEpisodesAsync(shows[i], cancellationToken);
                    await LogProgress(Math.Round(55 + 30 * (i + 1) / (double)shows.Count, 1));
                }
                catch (HttpException e)
                {
                    shows[i].TvdbFailed = true;
                    _showRepository.UpdateShow(shows[i]);

                    if (e.Message.Contains("(404) Not Found"))
                    {
                        await LogWarning($"Can't seem to find {shows[i].Name} on Tvdb, skipping show for now");
                    }
                }
                catch (Exception e)
                {
                    await LogError($"Can't seem to process show {shows[i].Name}, check the logs for more details!");
                    Logger.Error(e);
                    shows[i].TvdbFailed = true;
                    _showRepository.UpdateShow(shows[i]);
                }
            }
        }

        private async Task ProgressMissingEpisodesAsync(Show show, CancellationToken cancellationToken)
        {
            var missingEpisodes = new List<Episode>();
            var tvdbEpisodes = await _tvdbClient.GetEpisodes(show.TVDB, cancellationToken);

            foreach (var episode in tvdbEpisodes)
            {
                var seasons = show.Seasons.Where(x => x.IndexNumber == episode.SeasonNumber).ToList();

                Season season;
                if (!seasons.Any())
                {
                    Logger.Debug($"No season with index {episode.SeasonNumber} found for missing episode ({show.Name}), so we need to create one first");
                    season = ShowConverter.ConvertToSeason(episode.SeasonNumber, show);
                    _showRepository.InsertSeasonsBulk(new[] { season });
                    show.Seasons.Add(season);
                    _showRepository.UpdateShow(show);
                }
                else
                {
                    season = seasons.First();
                }

                if (IsEpisodeMissing(show.Episodes, season, episode))
                {
                    missingEpisodes.Add(episode.ConvertToEpisode(show, season));
                }
            }

            await LogInformation($"Found {missingEpisodes.Count} missing episodes for show {show.Name}");
            _showRepository.InsertEpisodesBulk(missingEpisodes);

            show.TvdbSynced = true;
            show.Episodes.AddRange(missingEpisodes);
            _showRepository.UpdateShow(show);
        }

        private static bool IsEpisodeMissing(IEnumerable<Episode> localEpisodes, Season season, VirtualEpisode tvdbEpisode)
        {
            if (season == null)
            {
                return true;
            }

            foreach (var localEpisode in localEpisodes)
            {
                if (localEpisode.ParentId == season.Id.ToString())
                {
                    if (!localEpisode.IndexNumberEnd.HasValue)
                    {
                        if (localEpisode.IndexNumber == tvdbEpisode.EpisodeNumber)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (localEpisode.IndexNumber <= tvdbEpisode.EpisodeNumber &&
                            localEpisode.IndexNumberEnd >= tvdbEpisode.EpisodeNumber)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        #endregion

        #region Helpers

        private async Task<bool> IsEmbyAliveAsync()
        {
            var result = await _embyClient.PingEmbyAsync(Settings.Emby.FullEmbyServerAddress);
            return result == "Emby Server";
        }

        private async Task CalculateStatistics()
        {
            await LogInformation("Calculating movie statistics");

            _statisticsRepository.MarkShowTypesAsInvalid();
            _statisticsRepository.MarkMovieTypesAsInvalid();

            var movieLibraries = _movieService.GetMovieLibraries().Select(x => x.Id).ToList();
            var movieLibrarySets = movieLibraries.PowerSets().ToList();
            for (var i = 0; i < movieLibrarySets.Count; i++)
            {
                await _movieService.CalculateMovieStatistics(movieLibrarySets[i].ToList());
                await LogProgress(Math.Round(85 + 8 * (i + 1) / (double)movieLibrarySets.Count, 1));
            }

            await LogInformation("Calculating show statistics");
            //var showLibraries = _showService.GetShowLibraries().Select(x => x.Id).ToList();
            //var showLibrarySets = showLibraries.PowerSets().ToList();
            //for (var i = 0; i < showLibrarySets.Count; i++)
            //{
            //    var libraryList = showLibrarySets[i].ToList();
            //    await _showService.CalculateShowStatistics(libraryList);
            //    _showService.CalculateCollectedRows(libraryList);

            //    await LogProgress(Math.Round(93 + 7 * (i + 1) / (double)showLibrarySets.Count, 1));
            //}
        }

        private async Task<List<Library>> GetLibrariesAsync()
        {
            await LogInformation("Asking Emby for all root folders");
            var rootItems = await _embyClient.GetMediaFoldersAsync();

            return rootItems.Items
                .Select(x => new Library
                {
                    Id = x.Id,
                    Name = x.Name,
                    Type = x.CollectionType.ToCollectionType(),
                    PrimaryImage = x.ImageTags.ContainsKey(ImageType.Primary) ? x.ImageTags.FirstOrDefault(y => y.Key == ImageType.Primary).Value : default(string)
                })
                .Where(x => x.Type != LibraryType.BoxSets)
                .ToList();
        }

        #endregion

        public void Dispose()
        {
            _embyClient?.Dispose();
        }
    }
}
