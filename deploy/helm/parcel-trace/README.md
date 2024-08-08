# parcel-trace Helm chart

Umbrella chart for all seven services. Every service is rendered from one reusable template
(`templates/_service.tpl`): ConfigMap, Deployment (probes on `/health` and `/health/ready`,
non-root, resource limits), ClusterIP Service and an optional `autoscaling/v2` HPA. One
`networking.k8s.io/v1` Ingress (NGINX class) exposes the frontend and the gateway; the other
services are cluster-internal.

| File | Purpose |
|---|---|
| `values.yaml` | baseline: images, env, secret key mapping, probes, ingress; workload identity off |
| `values-local.yaml` | any cluster (kind, Docker Desktop, …), no Azure: in-cluster datastores, plain Kubernetes Secret, workload identity off, `parcel-trace/*:local` images |
| `values-dev.yaml` | AKS dev: 1 replica each, in-cluster datastores, generated dev Secret, topic Job, workload identity on |
| `values-prod.yaml` | AKS prod: HPAs, TLS via cert-manager, managed datastores, Secret synced from Key Vault, workload identity on |

The Bitnami charts are declared in `Chart.yaml` behind `postgresql.enabled`, `mongodb.enabled`,
`redis.enabled` and `kafka.enabled`; local and dev turn them on.

The chart doesn't need Terraform. On Azure, the registry, Kafka endpoint, Secret and workload
identity client id can come from Terraform outputs or from resources that already exist
(`global.imageRegistry`, `global.env.Kafka__BootstrapServers`, `secrets.existingSecret`,
`workloadIdentity.clientId`).

```bash
helm dependency build deploy/helm/parcel-trace
for v in dev prod local; do
  helm lint deploy/helm/parcel-trace -f deploy/helm/parcel-trace/values-$v.yaml
  helm template pt deploy/helm/parcel-trace -f deploy/helm/parcel-trace/values-$v.yaml > /dev/null
done

# local cluster
helm upgrade --install pt deploy/helm/parcel-trace -f deploy/helm/parcel-trace/values-local.yaml -n parcel-trace --create-namespace
```

On Azure, deployments happen from the Azure DevOps pipeline (`HelmDeploy@0`, `upgrade --install`).
