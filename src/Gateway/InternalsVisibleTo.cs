using System.Runtime.CompilerServices;

// Grants OrderToCash.Gateway.UnitTests access to MongoOrderReadModel's
// internal BSON-mapping method, so MongoOrderReadModelMappingTests can drive
// it against hand-built BsonDocuments — no live MongoDB server, the same
// seam every other service's own InternalsVisibleTo.cs establishes for its
// own wire/mapping tests.
[assembly: InternalsVisibleTo("OrderToCash.Gateway.UnitTests")]
