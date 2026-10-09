using renting_room.Application.Fees;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Seeding;

/// <summary>Dựng khu: khu + bên cho thuê, khoản thu + bảng giá, phòng + công tơ, người thuê.</summary>
public sealed partial class DemoDataSeeder
{
    private const decimal ElectricityStart = 1000;
    private const decimal WaterStart = 100;

    private sealed record DemoRoom(Guid Id, Guid Electricity, Guid? Water)
    {
        public IReadOnlyList<Guid> Meters => Water is { } water ? [Electricity, water] : [Electricity];
    }

    /// <summary>PR-BR-17: chủ trọ khai thông tin của mình làm bên cho thuê một lần — khu không khai riêng dùng thông tin này.</summary>
    private Task SeedOwnerLessorAsync() =>
        Send(new UpdateOrganizationLessorCommand(LessorType.Individual, "Nguyễn Văn Chủ", "1 Tạ Quang Bửu, Hà Nội", _options.OwnerPhone, null,
            IdDocumentType.CitizenId, CitizenId(1), new DateOnly(2015, 6, 1), "Cục CSQLHC về TTXH", new DateOnly(1975, 3, 12),
            null, null, null, null, null));

    /// <param name="companyLessor">true = khu khai bên cho thuê riêng là công ty; false = dùng thông tin chủ trọ.</param>
    private async Task<Guid> PropertyAsync(string code, string name, string street, int anchorDay, ChargeMode chargeMode, bool companyLessor)
    {
        var id = await Send(new CreatePropertyCommand(code, name, new AddressInput(street, "Phường Bách Khoa", "Hà Nội", null, null),
            "Khu dữ liệu mẫu", null, null, new PropertyBillingInput(anchorDay, chargeMode, 5, ProrationMode.Daily, 30)));
        if (companyLessor)
            await Send(new UpdateLessorCommand(id, LessorType.Organization, "Công ty TNHH Nhà Trọ Demo", street + ", Hà Nội", "0900000010", null,
                null, null, null, null, null, "0101234567", "Nguyễn Văn Chủ", "Giám đốc", null, null));
        return id;
    }

    /// <summary>Id khoản thu theo tên (Điện / Nước được seed sẵn khi tạo khu).</summary>
    private async Task<Guid> FeeIdAsync(Guid propertyId, string name) =>
        (await Send(new ListFeeTypesQuery(propertyId, false))).Single(f => f.Name == name).Id;

    private Task AddPriceAsync(Guid feeTypeId, DateOnly from, decimal? unitPrice, string? note = null, IReadOnlyList<PriceTier>? tiers = null) =>
        Send(new AddFeePriceCommand(feeTypeId, new FeePriceInput(from, unitPrice, note, tiers)));

    private async Task<Guid> ServiceFeeAsync(
        Guid propertyId, string name, ChargeBasis basis, string unit, decimal price, DateOnly from, VehicleType? vehicleType = null) =>
        (await Send(new CreateFeeTypeCommand(propertyId, name, FeeGroup.Service, basis, unit, false,
            basis == ChargeBasis.PerUnit ? 1 : null, null, new FeePriceInput(from, price, null), vehicleType))).Id;

    private async Task<DemoRoom> RoomAsync(Guid propertyId, Guid electricityFee, Guid? waterFee, string code, string floor, decimal rent, DateOnly installed)
    {
        var roomId = await Send(new CreateRoomCommand(propertyId, code,
            new RoomSpecInput(floor, 20, 3, rent, rent, ["air_con", "water_heater", "wifi"], null)));
        var electricity = await MeterAsync(roomId, electricityFee, "E-" + code, installed, ElectricityStart);
        Guid? water = waterFee is { } fee ? await MeterAsync(roomId, fee, "W-" + code, installed, WaterStart) : null;
        return new DemoRoom(roomId, electricity, water);
    }

    private async Task<Guid> MeterAsync(Guid roomId, Guid feeTypeId, string serial, DateOnly installed, decimal initial)
    {
        var id = await Send(new InstallMeterCommand(roomId, feeTypeId, serial, installed, initial, null));
        _meterValues[id] = initial;
        return id;
    }

    private int _renterNo = 100;

    private Task<Guid> RenterAsync(
        string fullName, DateOnly dateOfBirth, Gender gender, string? phone,
        IdDocumentType idType = IdDocumentType.CitizenId, string? idNumber = null, string nationality = "VN", string? occupation = "Nhân viên văn phòng") =>
        Send(new CreateRenterCommand(new RenterInput(fullName, dateOfBirth, gender, phone, null, nationality, idType,
            idNumber ?? CitizenId(++_renterNo), new DateOnly(2021, 1, 15), "Cục CSQLHC về TTXH", "Nam Định", occupation, null,
            "Người thân", "0911000000", null)));

    private static string Phone(int n) => $"09{12_000_000 + n:D8}";
}
