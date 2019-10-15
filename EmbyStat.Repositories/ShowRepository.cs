﻿using System.Collections.Generic;
using System.Linq;
using EmbyStat.Common.Enums;
using EmbyStat.Common.Models.Entities;
using EmbyStat.Repositories.Interfaces;
using LiteDB;
using MediaBrowser.Model.Extensions;
using NLog;
using Logger = NLog.Logger;

namespace EmbyStat.Repositories
{
    public class ShowRepository : IShowRepository
    {
        private readonly LiteCollection<Show> _showCollection;
        private readonly LiteCollection<Season> _seasonCollection;
        private readonly LiteCollection<Episode> _episodeCollection;

        public ShowRepository(IDbContext context)
        {
            _showCollection = context.GetContext().GetCollection<Show>();
            _seasonCollection = context.GetContext().GetCollection<Season>();
            _episodeCollection = context.GetContext().GetCollection<Episode>();
        }

        public void RemoveShows()
        {
            _seasonCollection.DeleteMany(x => true);
            _episodeCollection.DeleteMany(x => true);
            _showCollection.DeleteMany(x => true);
        }

        public void InsertShow(Show show)
        {
            _showCollection.Insert(show);
        }

        public void InsertSeasonsBulk(IEnumerable<Season> seasons)
        {
            _seasonCollection.InsertBulk(seasons);
        }

        public void InsertEpisodesBulk(IEnumerable<Episode> episodes)
        {
            _episodeCollection.InsertBulk(episodes);
        }

        public void UpdateShow(Show show)
        {
            _showCollection.Update(show);
        }

        public IEnumerable<Show> GetAllShows(IReadOnlyList<string> collectionIds, bool includeSeasons, bool includeEpisodes)
        {
            var query = _showCollection;

            if (includeSeasons)
            {
                query = query.Include(x => x.Seasons);
            }

            if (includeEpisodes)
            {
                query = query.Include(x => x.Episodes);
            }

            if (collectionIds.Any())
            {
                var bArray = new BsonArray();
                foreach (var collectionId in collectionIds)
                {
                    bArray.Add(collectionId);
                }

                //TODO: klopt niet!
                return query.Find(Query.In("CollectionId", bArray));
            }

            return query.FindAll();
        }

        public Season GetSeasonById(string id)
        {
            return _seasonCollection.FindById(id);
        }

        public int GetShowCountForPerson(string personId)
        {
            return _showCollection.Count(Query.EQ("People[*]._id", personId));
        }

        public IEnumerable<Show> GetAllShowsWithTvdbId()
        {
            return _showCollection
                .Include(x => x.Episodes)
                .Include(x => x.Seasons)
                .Find(x => !string.IsNullOrWhiteSpace(x.TVDB));
        }

        public IEnumerable<Episode> GetAllEpisodesForShow(int showId)
        {
            return _episodeCollection.Find(Query.EQ("ShowId", showId));
        }

        public bool AnyShows()
        {
            return _showCollection.Exists(Query.All());
        }

        public Episode GetEpisodeById(string id)
        {
            return _episodeCollection.FindById(id);
        }
    }
}
