using renting_room.Domain.Common;

namespace renting_room.Domain.Contracts;

public sealed partial class Contract
{
    // Tài sản bàn giao & xe
    public Result<ContractAsset> AddAsset(AssetInput input)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure<ContractAsset>(ContractErrors.NotDraft);

        var asset = new ContractAsset(Id, input);
        _assets.Add(asset);
        return Result.Success(asset);
    }

    public Result UpdateAsset(Guid assetId, AssetInput input)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        var asset = _assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return Result.Failure(ContractErrors.AssetNotFound);

        asset.Update(input);
        return Result.Success();
    }

    public Result RemoveAsset(Guid assetId)
    {
        if (Status != ContractStatus.Draft)
            return Result.Failure(ContractErrors.NotDraft);

        return _assets.RemoveAll(a => a.Id == assetId) == 1
            ? Result.Success()
            : Result.Failure(ContractErrors.AssetNotFound);
    }

    public Result RecordAssetReturn(Guid assetId, string? condition)
    {
        if (Status != ContractStatus.Liquidating)
            return Result.Failure(ContractErrors.NotLiquidating);

        var asset = _assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null)
            return Result.Failure(ContractErrors.AssetNotFound);

        asset.RecordReturn(condition);
        return Result.Success();
    }

    public Result<ContractVehicle> RegisterVehicle(VehicleInput input)
    {
        if (Status is not (ContractStatus.Draft or ContractStatus.Active))
            return Result.Failure<ContractVehicle>(ContractErrors.NotEditable);
        if (input.RegisteredFrom < StartDate)
            return Result.Failure<ContractVehicle>(ContractErrors.DateOutsideContract);

        var vehicle = new ContractVehicle(Id, input);
        _vehicles.Add(vehicle);
        return Result.Success(vehicle);
    }

    public Result EndVehicle(Guid vehicleId, DateOnly endDate)
    {
        var vehicle = _vehicles.FirstOrDefault(v => v.Id == vehicleId);
        if (vehicle is null)
            return Result.Failure(ContractErrors.VehicleNotFound);
        if (!vehicle.IsActive)
            return Result.Failure(ContractErrors.VehicleAlreadyEnded);
        if (endDate < vehicle.RegisteredFrom)
            return Result.Failure(ContractErrors.DateOutsideContract);

        vehicle.End(endDate);
        return Result.Success();
    }
}
