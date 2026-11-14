using FluentValidation;
using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Domain.Contracts;

namespace renting_room.Application.UnitTests;

/// <summary>
/// M-19 (07/11/2026): chỉ số gửi kèm kích hoạt / chuyển phòng / hóa đơn trả phòng / thanh lý không có validator ⇒ số âm, quá lớn, 3 số lẻ
/// hoặc trùng <c>meterId</c> làm <c>Meter.GuardValue</c> / <c>ToDictionary</c> ném lỗi ⇒ 500. Phải là 400 theo từng trường.
/// </summary>
public sealed class MeterReadingInputValidationTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Guid MeterA = Guid.NewGuid();
    private static readonly Guid MeterB = Guid.NewGuid();

    public static TheoryData<string, decimal?> InvalidValues => new()
    {
        { "âm", -1m },
        { "quá lớn", 100_000_000m },
        { "3 số lẻ", 12.345m }
    };

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public void Activate_InvalidReadingValue_IsRejected(string _, decimal? value)
    {
        var result = new ActivateContractCommandValidator().Validate(
            new ActivateContractCommand(Guid.NewGuid(), [new MeterReadingInput(MeterA, value)]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "HandoverReadings[0].Value" && e.ErrorCode == "INVALID_READING");
    }

    [Fact]
    public void Activate_NullValueMeansLatest_AndTwoDecimalsAreAccepted()
    {
        var result = new ActivateContractCommandValidator().Validate(
            new ActivateContractCommand(Guid.NewGuid(), [new MeterReadingInput(MeterA, null), new MeterReadingInput(MeterB, 1234.56m)]));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Activate_DuplicateMeterId_IsRejected()
    {
        var result = new ActivateContractCommandValidator().Validate(
            new ActivateContractCommand(Guid.NewGuid(), [new MeterReadingInput(MeterA, 1m), new MeterReadingInput(MeterA, 2m)]));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "HandoverReadings" && e.ErrorCode == "DUPLICATE_METER");
    }

    [Fact]
    public void FinalInvoice_InvalidOrDuplicateReadings_AreRejected()
    {
        var validator = new CreateFinalInvoiceCommandValidator();

        validator.Validate(new CreateFinalInvoiceCommand(Guid.NewGuid(), [new MeterReadingInput(MeterA, -5m)]))
            .Errors.Should().ContainSingle(e => e.PropertyName == "FinalReadings[0].Value");
        validator.Validate(new CreateFinalInvoiceCommand(Guid.NewGuid(), [new MeterReadingInput(MeterA, 1m), new MeterReadingInput(MeterA, 1m)]))
            .Errors.Should().ContainSingle(e => e.ErrorCode == "DUPLICATE_METER");
    }

    [Fact]
    public void RoomTransfer_InvalidReadingsOnEitherRoom_AreRejected()
    {
        var validator = new TransferRoomCommandValidator();
        var command = new TransferRoomCommand(
            Guid.NewGuid(), Guid.NewGuid(), [new MeterReadingInput(MeterA, 1.234m)], [new MeterReadingInput(MeterB, -1m)], null, null);

        var result = validator.Validate(command);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("OldRoomReadings[0].Value", "NewRoomReadings[0].Value");
    }

    [Fact]
    public void StartLiquidation_InvalidFinalReading_IsRejected()
    {
        var result = new StartLiquidationCommandValidator().Validate(new StartLiquidationCommand(
            Guid.NewGuid(), new DateOnly(2026, 11, 1), TerminationReason.MutualAgreement, null, null, null, [new MeterReadingInput(MeterA, -1m)]));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "FinalReadings[0].Value");
    }

    [Fact]
    public void CompleteLiquidation_InvalidFinalReading_IsRejected()
    {
        var result = new CompleteLiquidationCommandValidator(new FixedClock(new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero))).Validate(
            new CompleteLiquidationCommand(Guid.NewGuid(), null, null, null, null, FinalReadings: [new MeterReadingInput(MeterA, 100_000_000m)]));

        result.Errors.Should().Contain(e => e.PropertyName == "FinalReadings[0].Value" && e.ErrorCode == "INVALID_READING");
    }
}
