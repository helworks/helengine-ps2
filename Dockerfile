# syntax=docker/dockerfile:1

FROM ubuntu:24.04

ARG DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y \
        bash \
        ca-certificates \
        curl \
        g++ \
        make \
        pkg-config \
        xorriso \
    && rm -rf /var/lib/apt/lists/*

ENV PS2DEV=/usr/local/ps2dev
ENV PS2SDK=${PS2DEV}/ps2sdk
ENV GSKIT=${PS2DEV}/gsKit
ENV PATH=${PATH}:${PS2DEV}/bin:${PS2DEV}/ee/bin:${PS2DEV}/iop/bin:${PS2DEV}/dvp/bin:${PS2SDK}/bin
ENV PKG_CONFIG_PATH=${GSKIT}/lib/pkgconfig:${PS2SDK}/ports/lib/pkgconfig:${PS2SDK}/lib/pkgconfig

RUN mkdir -p ${PS2DEV} \
    && curl -L https://github.com/ps2dev/ps2dev/releases/download/latest/ps2dev-ubuntu-latest.tar.gz -o /tmp/ps2dev-latest.tar.gz \
    && tar -xf /tmp/ps2dev-latest.tar.gz --strip-components 1 -C ${PS2DEV} \
    && find ${PS2DEV} -type f -name '*.pc' -exec sed -i 's|/__w/ps2dev/ps2dev/ps2dev|/usr/local/ps2dev|g' {} + \
    && rm -f /tmp/ps2dev-latest.tar.gz

WORKDIR /workspace
CMD ["/bin/bash"]
