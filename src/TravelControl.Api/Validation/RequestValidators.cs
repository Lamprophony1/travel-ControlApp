using FluentValidation;
using TravelControl.Application.Contracts;
using TravelControl.Domain;

namespace TravelControl.Api.Validation;

public sealed class CreatePassengerValidator : AbstractValidator<CreatePassengerRequest>
{
    public CreatePassengerValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(220);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.PassportNumber).MaximumLength(40);
    }
}

public sealed class UpdatePassengerValidator : AbstractValidator<UpdatePassengerRequest>
{
    public UpdatePassengerValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(220);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class FlightBaggageValidator : AbstractValidator<FlightBaggageRequest>
{
    public FlightBaggageValidator()
    {
        RuleFor(x => x.Status).Must(x => x != VerificationStatus.NotApplicable)
            .WithMessage("No aplica solo se admite como excepción documentada fuera del flujo normal.");
        RuleFor(x => x.CheckedBagCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CheckedBagWeightKg).GreaterThanOrEqualTo(0);
        When(x => x.Status == VerificationStatus.Confirmed, () =>
        {
            RuleFor(x => x.CheckedBagCount).GreaterThanOrEqualTo(1);
            RuleFor(x => x.CheckedBagWeightKg).GreaterThanOrEqualTo(23);
            RuleFor(x => x.AppliesOutbound).Equal(true);
            RuleFor(x => x.AppliesReturn).Equal(true);
        });
    }
}

public sealed class RoomUpdateValidator : AbstractValidator<RoomUpdateRequest>
{
    public RoomUpdateValidator()
    {
        RuleFor(x => x.InternalCode).NotEmpty().MaximumLength(60);
        RuleFor(x => x.CheckOut).GreaterThan(x => x.CheckIn).When(x => x.CheckIn.HasValue && x.CheckOut.HasValue);
        RuleFor(x => x.CapacityOverrideReason).NotEmpty().When(x => x.CapacityOverride);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Status == VerificationStatus.NotApplicable);
        When(x => x.Status == VerificationStatus.Confirmed, () =>
        {
            RuleFor(x => x.CheckIn).NotNull(); RuleFor(x => x.CheckOut).NotNull();
            RuleFor(x => x.RoomType).NotEmpty();
        });
    }
}

public sealed class FlightBookingValidator : AbstractValidator<FlightBookingRequest>
{
    public FlightBookingValidator()
    {
        RuleForEach(x => x.Segments).ChildRules(segment =>
        {
            segment.RuleFor(x => x.ArrivalAt).GreaterThan(x => x.DepartureAt).When(x => x.DepartureAt.HasValue && x.ArrivalAt.HasValue);
            segment.RuleFor(x => x.Sequence).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.Segments.Select(s => s.Sequence)).Must(x => x.Distinct().Count() == x.Count())
            .WithMessage("La secuencia de los segmentos no puede repetirse.");
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Status == VerificationStatus.NotApplicable);
        When(x => x.Status == VerificationStatus.Confirmed, () =>
        {
            RuleFor(x => x.Airline).NotEmpty(); RuleFor(x => x.Pnr).NotEmpty();
        });
    }
}

public sealed class BaggageUpdateValidator : AbstractValidator<BaggageUpdateRequest>
{
    public BaggageUpdateValidator()
    {
        RuleFor(x => x.CheckedBagCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.WeightPerBagKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExceptionReason).NotEmpty().When(x => x.Status == VerificationStatus.NotApplicable);
        When(x => x.Status == VerificationStatus.Confirmed, () =>
        {
            RuleFor(x => x.FlightBookingId).NotNull().WithMessage("Debe asociarse una reserva aérea.");
            RuleFor(x => x.CheckedBagCount).GreaterThanOrEqualTo(1);
            RuleFor(x => x.WeightPerBagKg).GreaterThanOrEqualTo(23);
            RuleFor(x => x).Must(x => x.AppliesOutbound && x.AppliesReturn || !string.IsNullOrWhiteSpace(x.ExceptionReason))
                .WithMessage("La franquicia debe cubrir ida y regreso o tener una excepción justificada.");
        });
    }
}
