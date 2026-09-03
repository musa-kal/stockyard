output "cluster_name" {
  description = "Name of the EKS cluster. Only known once the node group exists, so add-ons installed from outside the module have somewhere to schedule."
  value       = aws_eks_cluster.this.name

  depends_on = [aws_eks_node_group.default]
}

output "cluster_endpoint" {
  description = "API server endpoint."
  value       = aws_eks_cluster.this.endpoint
}

output "cluster_security_group_id" {
  description = "Security group EKS created for the control plane and managed nodes."
  value       = aws_eks_cluster.this.vpc_config[0].cluster_security_group_id
}

output "oidc_provider_arn" {
  description = "ARN of the IAM OIDC provider backing IRSA."
  value       = aws_iam_openid_connect_provider.this.arn
}

output "service_role_arns" {
  description = "IRSA role ARN per Kubernetes service account."
  value       = { for name, role in aws_iam_role.irsa : name => role.arn }
}

output "oidc_issuer" {
  description = "OIDC issuer host and path, without the scheme, as used in IRSA trust conditions."
  value       = local.oidc_issuer
}
