variable "domain" {
  type        = string
  description = "The domain registered at Scaleway Domains; its DNS zone holds the TEM records."
}

variable "organization_id" {
  type        = string
  description = "The Scaleway organisation that owns both projects."
}

variable "environments" {
  type        = map(string)
  description = "Environment name to its sending subdomain under var.domain."
  default = {
    staging    = "mail-staging"
    production = "mail"
  }
}
