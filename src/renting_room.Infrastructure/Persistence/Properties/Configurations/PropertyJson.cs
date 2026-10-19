using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using renting_room.Domain.Billing;
using renting_room.Domain.Identity;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Persistence.Configurations;

internal static class PropertyJson
{
    public static readonly JsonSerializerOptions Schedule = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
