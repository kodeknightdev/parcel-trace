variable "name" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "sku_name" {
  type    = string
  default = "B_Standard_B1ms"
}

variable "storage_mb" {
  type    = number
  default = 32768
}

variable "backup_retention_days" {
  type    = number
  default = 7
}

variable "geo_redundant_backup_enabled" {
  type    = bool
  default = false
}

variable "high_availability" {
  description = "Zone-redundant standby (needs a General Purpose or Memory Optimized SKU)."
  type        = bool
  default     = false
}

variable "zone" {
  type    = string
  default = "1"
}

variable "administrator_login" {
  type    = string
  default = "parceladmin"
}

variable "databases" {
  type    = list(string)
  default = ["identity", "shipments", "tracking"]
}

variable "tags" {
  type    = map(string)
  default = {}
}
