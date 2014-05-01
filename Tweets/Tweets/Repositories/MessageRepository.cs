using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Linq;
using Tweets.ModelBuilding;
using Tweets.Models;

namespace Tweets.Repositories
{
    public class MessageRepository : IMessageRepository
    {
        private readonly IMapper<Message, MessageDocument> messageDocumentMapper;
        private readonly MongoCollection<MessageDocument> messagesCollection;

        public MessageRepository(IMapper<Message, MessageDocument> messageDocumentMapper)
        {
            this.messageDocumentMapper = messageDocumentMapper;
            var connectionString = ConfigurationManager.ConnectionStrings["MongoDb"].ConnectionString;
            var databaseName = MongoUrl.Create(connectionString).DatabaseName;
            var database = new MongoClient(connectionString).GetServer().GetDatabase(databaseName);
            messagesCollection = database.GetCollection<MessageDocument>(MessageDocument.CollectionName);
        }

        public void Save(Message message)
        {
            var messageDocument = messageDocumentMapper.Map(message);
            messagesCollection.Insert(messageDocument);
        }

        public void Like(Guid messageId, User user)
        {
            var likeDocument = new LikeDocument {UserName = user.Name, CreateDate = DateTime.UtcNow};
            var query = Query.And(
                Query<MessageDocument>.EQ(doc => doc.Id, messageId),
                Query.Not(Query<MessageDocument>.ElemMatch(doc => doc.Likes,
                    builder => builder.EQ(doc => doc.UserName, user.Name))));
            var update = Update<MessageDocument>.Push(doc => doc.Likes, likeDocument);
            messagesCollection.Update(query, update);
        }

        public void Dislike(Guid messageId, User user)
        {
            var query = Query<MessageDocument>.EQ(doc => doc.Id, messageId);
            var update = Update<MessageDocument>.Pull(doc => doc.Likes, builder => builder.EQ(doc => doc.UserName, user.Name));
            messagesCollection.Update(query, update);
        }

        public IEnumerable<Message> GetPopularMessages()
        {
            var unwind = BsonDocument.Parse("{ $unwind: '$likes' }");
            var group = BsonDocument.Parse(
                "{ $group: { " +
                "   _id: {" +
                "       id: '$_id'," +
                "       userName: '$userName'," +
                "       text: '$text'," +
                "       createDate: '$createDate'" +
                "   }," +
                "   likesCount: { $sum: 1 }" +
                "}}");
            var rename = BsonDocument.Parse(
                "{ $project: {" +
                "   likesCount: 1," +
                "   userName: '$_id.userName'," +
                "   text: '$_id.text'," +
                "   createDate: '$_id.createDate'," +
                "   _id: '$_id.id'" +
                "}}");
            var hasNoLikes = BsonDocument.Parse("{ $match: { $or: [ { likes: null }, { '$likes': [] } ]}}");
            var projectNoLikes = BsonDocument.Parse(
                "{ $project: {" +
                "   likesCount: { $add: 0 }," +
                "   userName: 1," +
                "   text: 1," +
                "   createDate: 1," +
                "   _id: 1" +
                "}}");
            var sort = BsonDocument.Parse("{ $sort: { likesCount: -1}}");
            var limit = BsonDocument.Parse("{ $limit: 10 }");

            var documents = messagesCollection.Aggregate(unwind, group, rename, sort, limit).ResultDocuments.Concat(
                messagesCollection.Aggregate(hasNoLikes, projectNoLikes, sort, limit).ResultDocuments).Take(10).ToList();

            return documents.Select(doc =>
                new Message
                {
                    Id = Guid.Parse(doc["_id"].AsString),
                    CreateDate = doc["createDate"].ToUniversalTime(),
                    Text = doc["text"].AsString,
                    User = new User {Name = doc["userName"].AsString},
                    Likes = doc["likesCount"].AsInt32
                }).ToList();
        }

        public IEnumerable<UserMessage> GetMessages(User user)
        {
            return messagesCollection.AsQueryable()
                    .Where(doc => doc.UserName == user.Name)
                    .OrderByDescending(doc => doc.CreateDate)
                    .AsEnumerable()
                    .Select(doc =>
                    {
                        doc.Likes = doc.Likes == null ? new List<LikeDocument>(0) : doc.Likes.ToList();
                        return new UserMessage
                        {
                            CreateDate = doc.CreateDate,
                            Id = doc.Id,
                            Text = doc.Text,
                            User = user,
                            Likes = doc.Likes.Count(),
                            Liked = doc.Likes.Any(like => like.UserName == user.Name)
                        };
                    }).ToList();
        }
    }
}