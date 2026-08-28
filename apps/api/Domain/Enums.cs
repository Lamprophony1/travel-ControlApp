namespace TravelControl.Api.Domain;

public enum VerificationStatus { Confirmed, ToVerify, InProgress, NotIncluded, NotApplicable }
public enum PassengerOverallStatus { Ready, Pending, Attention }
public enum PassportStatus { Incomplete, Expired, ExpiringSoon, Valid }
public enum UserRole { Administrator, Editor, Viewer }
public enum OperatorType { Agency, HotelOperator, Airline, Transfer, Other }
public enum TransferCoverage { Arrival, Departure, Both }
public enum SegmentType { Outbound, Return }
public enum FollowUpStatus { Open, InProgress, Closed }
public enum FollowUpPriority { Low, Medium, High, Critical }
public enum DocumentType { Passport, AirTicket, HotelVoucher, BaggageProof, TransferVoucher, Other }

