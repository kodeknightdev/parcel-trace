variable "name" {
  description = "Resource group name, e.g. rg-parcel-trace-dev."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "tags" {
  description = "Tags applied to the resource group."
  type        = map(string)
  default     = {}
}
