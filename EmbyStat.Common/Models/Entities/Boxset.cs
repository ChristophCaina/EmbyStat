﻿using LiteDB;

namespace EmbyStat.Common.Models.Entities
{
    public class BoxSet
    {
        [BsonId]
	    public string Id { get; set; }
	    public string ParentId { get; set; }
	    public string Name { get; set; }
	    public string OfficialRating { get; set; }
	    public string Primary { get; set; }
    }
}
