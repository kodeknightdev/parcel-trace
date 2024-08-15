variable "name" {
  description = "3-24 characters, globally unique, e.g. kv-parceltrace-dev."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.name))
    error_message = "Key Vault names are 3-24 characters: letters, digits and dashes."
  }
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "soft_delete_retention_days" {
  type    = number
  default = 7
}

variable "purge_protection_enabled" {
  type    = bool
  default = false
}

variable "reader_object_ids" {
  description = "Principals granted Key Vault Secrets User, keyed by a stable label."
  type        = map(string)
  default     = {}
}

variable "secrets" {
  description = "Secrets to store, name => value."
  type        = map(string)
  default     = {}
  sensitive   = true
}

variable "tags" {
  type    = map(string)
  default = {}
}
