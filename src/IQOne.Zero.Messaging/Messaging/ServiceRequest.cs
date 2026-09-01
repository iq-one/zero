namespace IQOne.Zero.Messaging;

/// <summary>
/// Base of every service request.
/// </summary>
/// <remarks>
/// Deliberately empty. Fields that look universal — a tenant override, a cache bypass, a
/// correlation id — belong to a particular application's published contract, and baking
/// them in here would put one product's wire format into every product's requests. Derive
/// your own base and put them there.
/// </remarks>
public abstract class ServiceRequest;

/// <summary>A request whose response payload type is known at the declaration site.</summary>
/// <typeparam name="TResponseModel">The payload the handler returns.</typeparam>
public abstract class ServiceRequest<TResponseModel> : ServiceRequest;
