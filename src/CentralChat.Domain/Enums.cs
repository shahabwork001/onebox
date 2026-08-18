namespace CentralChat.Domain;

public enum ContactStatus { Active, Blocked, Archived }
public enum ConversationStatus { Open, Closed }
public enum TicketStatus { New, Open, Pending, Resolved, Closed }
public enum TicketPriority { Low, Normal, High, Urgent }
public enum MessageDirection { Inbound, Outbound }
public enum MessageStatus { Received, Queued, Sent, Delivered, Read, Failed }
public enum MessageType { Text, Image, Video, Audio, Document, Sticker, Location, Contact, Interactive, Reaction, Unknown }
public enum WebhookProcessingStatus { Pending, Published, Processing, Processed, Failed }
public enum AssignmentAction { Claimed, Assigned, Reassigned, Unassigned }
