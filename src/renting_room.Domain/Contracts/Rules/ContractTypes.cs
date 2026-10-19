namespace renting_room.Domain.Contracts;

public static class ContractTypes
{
    public static string DefaultTitle(ContractType type) => type switch
    {
        ContractType.WholeHouseRental => "HỢP ĐỒNG THUÊ NHÀ",
        _ => "HỢP ĐỒNG THUÊ PHÒNG TRỌ"
    };
}
