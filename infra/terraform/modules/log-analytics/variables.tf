variable "name" {
  type = string
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "retention_in_days" {
  description = "How long container logs and metrics are kept."
  type        = number
  default     = 30
}

variable "daily_quota_gb" {
  description = "Ingestion cap per day; -1 for unlimited."
  type        = number
  default     = -1
}

variable "tags" {
  type    = map(string)
  default = {}
}
