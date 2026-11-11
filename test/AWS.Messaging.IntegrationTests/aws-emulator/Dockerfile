# AWS Emulator (https://github.com/aws/aws-emulator) doesn't publish a prebuilt container
# image, so this builds one from its signed RPM package, as described in
# https://docs.aws.amazon.com/emulator/latest/userguide/getting-started-docker.html
#
# The version is pinned so that a new emulator release (they ship several a day) can't
# change test results unexpectedly; bump AWS_EMULATOR_VERSION deliberately. Published
# versions are listed at
# https://downloads.aws.com/aws-emulator/packages/aws-emulator/LTS/versions.json, where
# an entry such as "1.0.322.1" is version 1.0.322, package revision 1.
FROM public.ecr.aws/amazonlinux/amazonlinux:2023-minimal

ARG AWS_EMULATOR_VERSION=1.0.322
# Set automatically by BuildKit to the platform being built (amd64 or arm64).
ARG TARGETARCH

RUN set -eu; \
    case "${TARGETARCH:-amd64}" in \
      amd64) arch=x86_64 ;; \
      arm64) arch=aarch64 ;; \
      *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSLo /tmp/aws-emulator.rpm \
      "https://downloads.aws.com/aws-emulator/packages/aws-emulator/LTS/aws-emulator-${AWS_EMULATOR_VERSION}-1-${arch}.rpm"; \
    rpm -i /tmp/aws-emulator.rpm; \
    rm /tmp/aws-emulator.rpm; \
    mkdir -p /home/emulator; \
    chown 1000 /home/emulator

USER 1000
ENV HOME=/home/emulator
EXPOSE 9297
ENTRYPOINT ["aws-emulator", "-b", "0.0.0.0"]
