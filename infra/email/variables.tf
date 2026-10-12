variable "domain" {
  description = "The domain registered at Scaleway Domains; its DNS zone holds the TEM records."
  type        = string
}

variable "organization_id" {
  description = "The Scaleway organisation that owns both projects."
  type        = string
}

variable "environments" {
  description = "Environment name to its sending subdomain under var.domain."
  type        = map(string)
  default = {
    staging    = "mail-staging"
    production = "mail"
  }
}
