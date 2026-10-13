variable "queue_name" {
  description = "Name of the events queue. The dead-letter queue takes the same name with a -dlq suffix."
  type        = string
  default     = "stockyard-events"
}

variable "max_receive_count" {
  description = "Receives before a message moves to the dead-letter queue."
  type        = number
  default     = 5
}

variable "visibility_timeout_seconds" {
  description = "How long a received message stays hidden from other consumers."
  type        = number
  default     = 60
}

variable "message_retention_seconds" {
  description = "Retention on the main queue."
  type        = number
  default     = 345600
}

variable "dlq_retention_seconds" {
  description = "Retention on the dead-letter queue. Kept at the 14 day maximum so a failure noticed late can still be replayed."
  type        = number
  default     = 1209600
}
