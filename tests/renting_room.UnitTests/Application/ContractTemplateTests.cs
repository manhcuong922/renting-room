using System.Text.Json;
using FluentValidation;
using renting_room.Application.Contracts;
using renting_room.Domain.Contracts;

namespace renting_room.UnitTests.Application;

public sealed class CustomFieldValuesTests
{
    private static readonly CustomFieldDefinition[] Definitions =
    [
        new("water_price", "Tiền nước", CustomFieldType.Money, true, null, "đ/người", null),
        new("electricity_pricing", "Cách tính điện", CustomFieldType.Select, false, ["Giá nhà nước", "Giá cố định"], null, null),
        new("wifi_included", "Có wifi", CustomFieldType.Boolean, false, null, null, null),
        new("handover_date", "Ngày bàn giao", CustomFieldType.Date, false, null, null, null),
        new("floor_area", "Diện tích", CustomFieldType.Number, false, null, "m²", null),
        new("note", "Ghi chú", CustomFieldType.Text, false, null, null, null)
    ];

    private static Dictionary<string, JsonElement> Values(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    private static IReadOnlyList<string> FailedKeys(Action act) =>
        act.Should().Throw<ValidationException>().Which.Errors.Select(e => e.PropertyName).ToList();

    [Fact]
    public void ValidValues_AreNormalizedToTypedJson()
    {
        var json = CustomFieldValues.Normalize(Definitions, Values(new
        {
            water_price = 20000,
            electricity_pricing = " Giá nhà nước ",
            wifi_included = true,
            handover_date = "2026-02-01",
            floor_area = 45.5,
            note = "  Có ban công  "
        }), "contract.customFields");

        var stored = ContractDocumentJson.Values(json);
        stored["water_price"].GetDecimal().Should().Be(20000);
        stored["electricity_pricing"].GetString().Should().Be("Giá nhà nước");
        stored["wifi_included"].GetBoolean().Should().BeTrue();
        stored["handover_date"].GetString().Should().Be("2026-02-01");
        stored["floor_area"].GetDecimal().Should().Be(45.5m);
        stored["note"].GetString().Should().Be("Có ban công");
    }

    [Fact]
    public void MissingRequired_UnknownKey_AndWrongTypes_AreReportedPerField()
    {
        var keys = FailedKeys(() => CustomFieldValues.Normalize(Definitions, Values(new
        {
            electricity_pricing = "Miễn phí",
            wifi_included = "yes",
            handover_date = "01/02/2026",
            floor_area = "abc",
            unknown_field = 1
        }), "contract.customFields"));

        keys.Should().BeEquivalentTo(
            "contract.customFields.water_price",
            "contract.customFields.electricity_pricing",
            "contract.customFields.wifi_included",
            "contract.customFields.handover_date",
            "contract.customFields.floor_area",
            "contract.customFields.unknown_field");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1500.5)]
    [InlineData(2_000_000_000)]
    public void Money_MustBeWholeNumberWithinRange(decimal amount)
    {
        FailedKeys(() => CustomFieldValues.Normalize(Definitions, Values(new { water_price = amount }), "f"))
            .Should().Equal("f.water_price");
    }

    [Fact]
    public void EmptyOptionalValues_AreDropped_AndNoValuesGiveNull()
    {
        var optional = Definitions.Where(d => !d.Required).ToList();

        CustomFieldValues.Normalize(optional, Values(new { note = "   ", wifi_included = (bool?)null }), "f").Should().BeNull();
        CustomFieldValues.Normalize([], null, "f").Should().BeNull();
    }
}

public sealed class ContractTemplateValidatorTests
{
    private readonly CreateContractTemplateCommandValidator _validator = new();

    private static ContractTemplateInput Input(params CustomFieldDefinition[] fields) =>
        new("Thuê trọ", ContractType.RoomRental, "HỢP ĐỒNG THUÊ PHÒNG TRỌ", [new ContractClause("Trách nhiệm bên A", "- Bàn giao phòng")], fields);

    [Fact]
    public void Presets_AreValid()
    {
        foreach (var preset in ContractTemplatePresets.All)
            _validator.Validate(new CreateContractTemplateCommand(preset)).Errors.Should().BeEmpty(preset.Name);
    }

    [Fact]
    public void DuplicateKeys_InvalidKey_AndSelectWithoutOptions_AreRejected()
    {
        var result = _validator.Validate(new CreateContractTemplateCommand(Input(
            new("water_price", "Tiền nước", CustomFieldType.Money, true, null, null, null),
            new("water_price", "Tiền nước 2", CustomFieldType.Money, false, null, null, null),
            new("Tiền Điện", "Điện", CustomFieldType.Text, false, null, null, null),
            new("pricing", "Cách tính", CustomFieldType.Select, false, [], null, null),
            new("note", "Ghi chú", CustomFieldType.Text, false, ["a"], null, null))));

        result.Errors.Select(e => e.ErrorCode).Should().Contain(["DUPLICATE_FIELD_KEY", "INVALID_FORMAT", "INVALID_OPTIONS"]);
        result.Errors.Select(e => e.PropertyName).Should().Contain(["fields", "fields[2].Key", "fields[3].Options", "fields[4].Options"]);
    }

    [Fact]
    public void ClauseWithoutBody_IsRejected()
    {
        var input = Input() with { Clauses = [new ContractClause("Trách nhiệm chung", " ")] };

        _validator.Validate(new CreateContractTemplateCommand(input)).Errors
            .Should().ContainSingle(e => e.PropertyName == "clauses[0].Body");
    }
}
