using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Tweets.Models
{
    public class LikeDocument
    {
        [BsonElement("userName")]
        public string UserName { get; set; }

        [BsonElement("createDate")]
        public DateTime CreateDate { get; set; }
    }
}