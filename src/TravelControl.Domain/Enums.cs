namespace TravelControl.Domain;

public enum VerificationStatus { Confirmed, ToVerify, InProgress, NotIncluded, NotApplicable }
public enum PassengerOverallStatus { Ready, Pending, Attention }
public enum TripOverallStatus { Ready, Pending, Attention }
public enum PassportStatus { Incomplete, Expired, ExpiringSoon, Valid }
public enum UserRole { Administrator, Editor, Viewer }
public enum OperatorType { Agency, HotelOperator, Airline, Other }
public enum SegmentType { Outbound, Return }
public enum FollowUpStatus { Open, InProgress, Closed }
public enum FollowUpPriority { Low, Medium, High, Critical }
public enum DocumentType { Passport, AirTicket, HotelVoucher, BaggageProof, Other }
public enum TicketAccessStatus { Missing, Generated, Verified, Invalid }
